using System.Net;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Catalog;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Catalog;

/// <summary>
/// What an override changes downstream (ADR 0018): the resolver's answer, the slot length and count per person, the
/// booked snapshot, "any staff" skipping someone who does not offer the service, a refused booking for that person, the
/// public price list's "from" price and the admin book-for-client summary. Anna and Tom work every day 09:00–17:00;
/// "Cut" is 45 minutes at £40; bookings are made for tomorrow so notice and advance rules never interfere.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class StaffOverrideBookingTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _clientId;
    private Guid _cutId;
    private Guid _annaId;
    private Guid _tomId;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        await SeedSalonAsync(_studio.Id);
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_resolver_reads_the_override_through_the_tenant_filter()
    {
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, 45m, offers: true);

        var anna = await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IServiceOfferingResolver>().ResolveAsync(_cutId, _annaId));
        var tom = await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IServiceOfferingResolver>().ResolveAsync(_cutId, _tomId));
        var both = await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IServiceOfferingResolver>().ResolveAsync(_cutId, [_annaId, _tomId]));
        var spans = await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IServiceOfferingResolver>().PriceSpansAsync([_cutId]));

        anna.ShouldBe(new ServiceOffering(_cutId, _annaId, true, Duration.FromMinutes(60), 45m, "GBP", true));
        tom.ShouldBe(new ServiceOffering(_cutId, _tomId, true, Duration.FromMinutes(45), 40m, "GBP", false));
        both.Keys.ShouldBe([_annaId, _tomId], ignoreOrder: true);
        both[_annaId].ShouldBe(anna);
        both[_tomId].ShouldBe(tom);
        spans[_cutId].ShouldBe(new PriceSpan(40m, 45m));

        // Another salon never sees the row, and the service itself is unknown there.
        await Should.ThrowAsync<InvalidOperationException>(() => InTenantAsync(_polish.Id, sp => sp.GetRequiredService<IServiceOfferingResolver>().ResolveAsync(_cutId, _annaId)));
    }

    [Fact]
    public async Task An_override_changes_that_persons_slot_length_and_nobody_elses()
    {
        var before = await SlotsAsync(_studio.Id, _annaId);
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, null, offers: true);

        var annas = await SlotsAsync(_studio.Id, _annaId);
        var toms = await SlotsAsync(_studio.Id, _tomId);

        before.ShouldAllBe(s => s.End - s.Start == Duration.FromMinutes(45));
        annas.ShouldAllBe(s => s.End - s.Start == Duration.FromMinutes(60));
        annas.Count.ShouldBeLessThan(before.Count, "an hour fits fewer times into the day than 45 minutes");
        Local(annas).Last().ShouldBe(new LocalTime(16, 0));
        toms.ShouldAllBe(s => s.End - s.Start == Duration.FromMinutes(45));
        Local(toms).Last().ShouldBe(new LocalTime(16, 15));

        var any = await SlotsAsync(_studio.Id, null);
        any.ShouldAllBe(s => s.End - s.Start == (s.StaffMemberId == _annaId ? Duration.FromMinutes(60) : Duration.FromMinutes(45)));
    }

    [Fact]
    public async Task The_booked_snapshot_uses_the_override_for_that_person_and_the_defaults_for_others()
    {
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, 45m, offers: true);

        var anna = await BookAsync(_studio.Id, _annaId, At(10, 0));
        var tom = await BookAsync(_studio.Id, _tomId, At(10, 0));

        anna.End.ShouldBe(At(11, 0));
        anna.DurationMinutes.ShouldBe(60);
        anna.PriceAmount.ShouldBe(45m);
        anna.Currency.ShouldBe("GBP");
        tom.End.ShouldBe(At(10, 45));
        tom.DurationMinutes.ShouldBe(45);
        tom.PriceAmount.ShouldBe(40m);

        // Anna's next free start follows her hour, not the list's 45 minutes.
        Local(await SlotsAsync(_studio.Id, _annaId)).Where(t => t >= new LocalTime(10, 0)).First().ShouldBe(new LocalTime(11, 0));
        // A booking at a start the longer duration no longer allows is refused like any other stale slot.
        await Should.ThrowAsync<SlotNoLongerAvailableException>(() => BookAsync(_studio.Id, _annaId, At(16, 15)));
    }

    [Fact]
    public async Task Someone_who_does_not_offer_the_service_gets_no_slots_is_skipped_by_any_staff_and_cannot_be_booked()
    {
        await AddOverrideAsync(_studio.Id, _tomId, _cutId, null, null, offers: false);

        (await SlotsAsync(_studio.Id, _tomId)).ShouldBeEmpty();
        var any = await SlotsAsync(_studio.Id, null);
        any.ShouldNotBeEmpty();
        any.ShouldAllBe(s => s.StaffMemberId == _annaId);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => BookAsync(_studio.Id, _tomId, At(10, 0)));
        refused.ShouldNotBeOfType<SlotNoLongerAvailableException>();
        await BookAsync(_studio.Id, _annaId, At(10, 0));
        (await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<SalonDbContext>().Appointments.CountAsync())).ShouldBe(1);

        var tom = await InTenantAsync(_studio.Id, sp => sp.GetRequiredService<IServiceOfferingResolver>().ResolveAsync(_cutId, _tomId));
        tom.Offers.ShouldBeFalse();
    }

    [Fact]
    public async Task When_nobody_offers_the_service_there_are_no_slots_at_all()
    {
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, null, null, offers: false);
        await AddOverrideAsync(_studio.Id, _tomId, _cutId, null, null, offers: false);

        (await SlotsAsync(_studio.Id, null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_public_list_shows_a_from_price_only_while_prices_differ_between_staff()
    {
        using var visitor = factory.ClientFor(HostA);
        PriceTag(await GetAsync(visitor, "/services")).ShouldBe("£40.00");

        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, 45m, offers: true);
        var withOverride = await GetAsync(visitor, "/services");
        PriceTag(withOverride).ShouldBe("from £40.00");
        withOverride.ShouldContain("data-from=\"true\"");
        withOverride.ShouldContain("45 min", "the default duration stays on the card");

        // Tom stops offering it: only Anna is left, so her price is the price.
        await AddOverrideAsync(_studio.Id, _tomId, _cutId, null, null, offers: false);
        PriceTag(await GetAsync(visitor, "/services")).ShouldBe("£45.00");
    }

    [Fact]
    public async Task A_Polish_salon_says_od_before_the_cheapest_price()
    {
        var (service, anna, _) = await SeedSalonAsync(_polish.Id, "PLN", 80m);
        await AddOverrideAsync(_polish.Id, anna, service, null, 95m, offers: true);
        using var visitor = factory.ClientFor("salon-pl.localhost");

        var html = WebUtility.HtmlDecode(await GetAsync(visitor, "/services"));

        html.ShouldContain("od 80,00 zł");
    }

    [Fact]
    public async Task The_admin_booking_form_shows_the_effective_duration_and_price_for_the_chosen_person()
    {
        await AddOverrideAsync(_studio.Id, _annaId, _cutId, 60, 45m, offers: true);
        await AddOverrideAsync(_studio.Id, _tomId, _cutId, null, null, offers: false);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var day = LocalDatePattern.Iso.Format(_day);

        var anna = await GetAsync(client, $"/admin/appointments/new?clientId={_clientId}&serviceId={_cutId}&staffId={_annaId}&date={day}");
        var tom = await GetAsync(client, $"/admin/appointments/new?clientId={_clientId}&serviceId={_cutId}&staffId={_tomId}&date={day}");
        var any = await GetAsync(client, $"/admin/appointments/new?clientId={_clientId}&serviceId={_cutId}&date={day}");

        Offering(anna).ShouldBe("60 min · £45.00");
        Offering(tom).ShouldBe("Tom Reed does not offer this service.");
        tom.ShouldNotContain("data-testid=\"slot\"");
        Offering(any).ShouldBe("45 min · £45.00", "with Tom out, every bookable person charges £45 and the list duration is shown");
        any.ShouldContain("Anna Kowalska");

        await RemoveOverridesAsync(_studio.Id, _tomId);
        var anyAgain = await GetAsync(client, $"/admin/appointments/new?clientId={_clientId}&serviceId={_cutId}&date={day}");
        Offering(anyAgain).ShouldBe("45 min · from £40.00");
    }

    private static string PriceTag(string html)
    {
        var match = Regex.Match(html, "<span[^>]*data-testid=\"service-price\"[^>]*>(?<text>[^<]*)</span>");
        match.Success.ShouldBeTrue("no price on the card");
        return WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
    }

    private static string Offering(string html)
    {
        var match = Regex.Match(html, "<p[^>]*data-testid=\"offering\"[^>]*>(?<text>[^<]*)</p>");
        match.Success.ShouldBeTrue("no offering summary on the page");
        return Regex.Replace(WebUtility.HtmlDecode(match.Groups["text"].Value).Trim(), "\\s+", " ");
    }

    private static List<LocalTime> Local(IReadOnlyList<Slot> slots) => slots.Select(s => s.Start.InZone(London).TimeOfDay).ToList();

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return await response.Content.ReadAsStringAsync();
    }

    private Instant At(int hour, int minute) => London.AtLeniently(_day.At(new LocalTime(hour, minute))).ToInstant();

    private Task<IReadOnlyList<Slot>> SlotsAsync(Guid tenantId, Guid? staffId)
        => InTenantAsync(tenantId, sp => sp.GetRequiredService<IAvailabilityService>().GetSlotsAsync(_cutId, staffId, _day, _day));

    private Task<Appointment> BookAsync(Guid tenantId, Guid staffId, Instant start)
        => InTenantAsync(tenantId, sp => sp.GetRequiredService<IBookingService>().BookAsync(new BookAppointmentRequest(_clientId, staffId, _cutId, start, AppointmentSource.Admin)));

    private async Task<T> InTenantAsync<T>(Guid tenantId, Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await action(scope.ServiceProvider);
    }

    private async Task<(Guid ServiceId, Guid AnnaId, Guid TomId)> SeedSalonAsync(Guid tenantId, string currency = "GBP", decimal price = 40m)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var anna = new StaffMember { DisplayName = "Anna Kowalska", SortOrder = 1, CreatedAt = now };
        var tom = new StaffMember { DisplayName = "Tom Reed", SortOrder = 2, CreatedAt = now };
        db.StaffMembers.AddRange(anna, tom);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = tom.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(45), Price = price, Currency = currency, SortOrder = 1 };
        var client = new Client { FirstName = "Olivia", LastName = "Brown", CreatedAt = now, UpdatedAt = now };
        db.Services.Add(cut);
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        if (tenantId == _studio.Id)
        {
            (_clientId, _cutId, _annaId, _tomId) = (client.Id, cut.Id, anna.Id, tom.Id);
        }

        return (cut.Id, anna.Id, tom.Id);
    }

    private async Task AddOverrideAsync(Guid tenantId, Guid staffId, Guid serviceId, int? minutes, decimal? price, bool offers)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.StaffServiceOverrides.Add(new StaffServiceOverride
        {
            StaffMemberId = staffId,
            ServiceId = serviceId,
            Duration = minutes is { } m ? Duration.FromMinutes(m) : null,
            Price = price,
            Offers = offers,
        });
        await db.SaveChangesAsync();
    }

    private async Task RemoveOverridesAsync(Guid tenantId, Guid staffId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.StaffServiceOverrides.RemoveRange(await db.StaffServiceOverrides.Where(o => o.StaffMemberId == staffId).ToListAsync());
        await db.SaveChangesAsync();
    }
}
