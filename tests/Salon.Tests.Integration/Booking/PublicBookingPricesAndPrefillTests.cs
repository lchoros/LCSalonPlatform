using System.Net;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Staff;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;
using Salon.Web.Framework.Booking;

namespace Salon.Tests.Integration.Booking;

/// <summary>Staff-specific prices in the public flow (ADR 0018) and the device-only prefill for returning clients (ADR 0014).</summary>
public partial class PublicBookingTests
{
    [Fact]
    public async Task The_time_picker_and_the_details_quote_the_person_s_own_price_and_from_under_anyone()
    {
        await AddOverridesAsync(_studio.Id, tomPrice: 25m, tomMinutes: 45, priyaOffers: false);
        using var client = factory.ClientFor(HostA);

        var anyone = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}");
        var tomOnly = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={_tomId}");
        var annaOnly = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={_annaId}");
        var tomDetails = await GetAsync(client, DetailsPath(At(10, 0), _tomId));
        var anyDetails = await GetAsync(client, DetailsPath(At(10, 0), null));

        anyone.ShouldContain("from £20.00", Case.Sensitive, "Anna charges the list price, Tom more");
        anyone.ShouldContain("30 min");
        anyone.ShouldNotContain("Priya", Case.Sensitive, "she does not offer the service, so she is not on the picker");
        tomOnly.ShouldContain("£25.00");
        tomOnly.ShouldNotContain("from £");
        tomOnly.ShouldContain("45 min");
        annaOnly.ShouldContain("£20.00");
        annaOnly.ShouldNotContain("from £");
        tomDetails.ShouldContain("£25.00");
        tomDetails.ShouldContain("45 min");
        anyDetails.ShouldContain("from £20.00");

        // Booking Tom confirms what was quoted: the snapshot and the confirmation page carry his price.
        var (appointment, donePath) = await BookThroughTheFlowAsync(client, At(11, 0), _tomId);
        appointment.PriceAmount.ShouldBe(25m);
        appointment.DurationMinutes.ShouldBe(45);
        (await GetAsync(client, donePath)).ShouldContain("data-testid=\"done-price\">£25.00<");
    }

    [Fact]
    public async Task Someone_who_does_not_offer_the_service_cannot_be_picked_through_the_url()
    {
        await AddOverridesAsync(_studio.Id, tomPrice: null, tomMinutes: null, priyaOffers: false);
        var priyaId = await StaffIdAsync(_studio.Id, "Priya");
        using var client = factory.ClientFor(HostA);

        var picker = await GetAsync(client, $"/book/{_serviceId}?date={Iso(_day)}&staff={priyaId}");
        var details = await client.GetAsync(new Uri(DetailsPath(At(10, 0), priyaId), UriKind.Relative));

        picker.ShouldContain("data-testid=\"staff-any\"", Case.Sensitive, "falls back to anyone instead of an empty page");
        picker.ShouldNotContain($"staff={priyaId}");
        details.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_details_post_remembers_the_device_which_prefills_the_next_booking_until_forgotten()
    {
        using var client = factory.ClientFor(HostA);

        var fresh = await GetAsync(client, DetailsPath(At(10, 0), null));
        fresh.ShouldNotContain("data-testid=\"prefilled\"");
        fresh.ShouldNotContain("value=\"Olivia\"");

        var response = await PostDetailsAsync(client, Fields(At(10, 0)));
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cookie = response.Headers.GetValues("Set-Cookie").ShouldHaveSingleItem(ReturningClientCookie.CookieName + " is the only cookie the flow sets");
        cookie.ShouldStartWith(ReturningClientCookie.CookieName + "=");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("path=/book", Case.Insensitive);
        cookie.ShouldNotContain("Olivia", Case.Sensitive, "the details are protected, not readable from the cookie");

        var again = await GetAsync(client, DetailsPath(At(11, 0), null));
        again.ShouldContain("data-testid=\"prefilled\"");
        again.ShouldContain("Welcome back, Olivia");
        again.ShouldContain("value=\"Olivia\"");
        again.ShouldContain("value=\"Brown\"");
        again.ShouldContain($"value=\"{Phone}\"");
        again.ShouldContain("value=\"Olivia@Example.com\"");
        again.ShouldContain("forget=true", Case.Sensitive, "the Not you? link clears the cookie");

        var forgotten = await GetAsync(client, DetailsPath(At(11, 0), null) + "&forget=true");
        forgotten.ShouldNotContain("data-testid=\"prefilled\"");
        forgotten.ShouldNotContain("value=\"Olivia\"");
        (await GetAsync(client, DetailsPath(At(12, 0), null))).ShouldNotContain("data-testid=\"prefilled\"", Case.Sensitive, "the cookie is gone for good");
    }

    [Fact]
    public async Task A_cookie_from_another_salon_or_a_tampered_one_prefills_nothing()
    {
        using var studio = factory.ClientFor(HostA);
        await PostDetailsAsync(studio, Fields(At(10, 0)));
        var cookie = (await studio.GetAsync(new Uri(DetailsPath(At(11, 0), null), UriKind.Relative))).RequestMessage!.Headers.TryGetValues("Cookie", out var sent)
            ? string.Join("; ", sent)
            : null;
        cookie.ShouldNotBeNull().ShouldContain(ReturningClientCookie.CookieName);

        using var polish = factory.ClientFor(HostB);
        polish.DefaultRequestHeaders.Add("Cookie", cookie);
        var (_, _, polishService) = await SeedSalonAsync(_polish.Id);
        var other = await GetAsync(polish, $"/book/{polishService}/details?start={Uri.EscapeDataString(NodaTime.Text.InstantPattern.ExtendedIso.Format(At(10, 0)))}");
        other.ShouldNotContain("data-testid=\"prefilled\"", Case.Sensitive, "written for Studio 20, useless for Salon PL");

        using var tampered = factory.ClientFor(HostA);
        tampered.DefaultRequestHeaders.Add("Cookie", ReturningClientCookie.CookieName + "=edited-by-hand");
        (await GetAsync(tampered, DetailsPath(At(11, 0), null))).ShouldNotContain("data-testid=\"prefilled\"");
    }

    private async Task AddOverridesAsync(Guid tenantId, decimal? tomPrice, int? tomMinutes, bool priyaOffers)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var priya = new StaffMember { DisplayName = "Priya", SortOrder = 3, CreatedAt = SystemClock.Instance.GetCurrentInstant() };
        db.StaffMembers.Add(priya);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = priya.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        if (tomPrice is not null || tomMinutes is not null)
        {
            db.StaffServiceOverrides.Add(new StaffServiceOverride
            {
                TenantId = tenantId,
                StaffMemberId = _tomId,
                ServiceId = _serviceId,
                Price = tomPrice,
                Duration = tomMinutes is { } m ? Duration.FromMinutes(m) : null,
            });
        }

        db.StaffServiceOverrides.Add(new StaffServiceOverride { TenantId = tenantId, StaffMemberId = priya.Id, ServiceId = _serviceId, Offers = priyaOffers });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> StaffIdAsync(Guid tenantId, string displayName)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        return (await db.StaffMembers.SingleAsync(s => s.DisplayName == displayName)).Id;
    }
}