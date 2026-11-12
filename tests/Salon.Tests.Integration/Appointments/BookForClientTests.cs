using System.Net;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Appointments;
using Salon.Core.Appointments.Events;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// The admin "book for a client" form over HTTP (ADR 0012, admin booking slice): the client picker searches through the
/// client service, the slot list is exactly what <see cref="IAvailabilityService"/> offers in the salon zone, a posted
/// slot books through <see cref="IBookingService"/> (row, history, outbox) and lands on that day's list, a slot taken
/// meanwhile re-renders with a localized message and fresh slots, validation messages follow the salon language, and the
/// gates hold (Bookings.Manage + Clients.View, tenant, anti-forgery). Each salon has Anna (every day 09:00–17:00), a
/// 30-minute service and one client; bookings are made for tomorrow so notice and advance rules never interfere.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class BookForClientTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string FormPath = "/admin/appointments/new";
    private const string ListPath = "/admin/appointments";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone Warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private SalonSetup _a = null!;
    private SalonSetup _b = null!;
    private LocalDate _day;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _a = await SeedSalonAsync(_studio.Id, "Olivia", "Brown", "+447700900123", "GBP");
        _b = await SeedSalonAsync(_polish.Id, "Anna", "Nowak", "+48501234567", "PLN");
        _day = SystemClock.Instance.GetCurrentInstant().InZone(London).Date.PlusDays(1);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_finds_clients_and_lists_the_slots_the_availability_service_offers()
    {
        await BookDirectlyAsync(_studio.Id, _a, At(London, 10, 0));
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var search = await GetAsync(client, $"{FormPath}?q=Oli&date={Iso(_day)}");
        search.ShouldContain("data-testid=\"client-match\"");
        search.ShouldContain("Olivia Brown");
        search.ShouldContain($"clientId={_a.ClientId}");
        (await GetAsync(client, $"{FormPath}?q=zzz")).ShouldContain("No clients match “zzz”.");

        var form = await GetAsync(client, $"{FormPath}?clientId={_a.ClientId}&serviceId={_a.ServiceId}&date={Iso(_day)}");

        form.ShouldContain("data-testid=\"selected-client\"");
        form.ShouldContain("Olivia Brown");
        form.ShouldContain("Cut");
        form.ShouldContain("Anna Kowalska");
        form.ShouldContain("Any available");
        form.ShouldContain("__RequestVerificationToken");
        var expected = await SlotsAsync(_studio.Id, _a, null);
        expected.ShouldNotBeEmpty();
        var offered = SlotStarts(form);
        offered.ShouldBe(expected.Select(s => s.Start).ToList());
        offered.ShouldNotContain(At(London, 10, 0), "the booked slot is gone");
        form.ShouldContain("09:00");
        form.ShouldContain("16:30");
        form.ShouldNotContain("10:00<");

        var polishForm = await GetAsync(await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner), $"{FormPath}?clientId={_b.ClientId}&serviceId={_b.ServiceId}&date={Iso(_day)}");
        polishForm.ShouldContain("Zarezerwuj dla klienta");
        polishForm.ShouldContain("Dowolny dostępny");
        SlotStarts(polishForm).ShouldBe((await SlotsAsync(_polish.Id, _b, null)).Select(s => s.Start).ToList());
        polishForm.ShouldNotContain("Olivia Brown");
    }

    [Fact]
    public async Task Posting_a_slot_books_the_appointment_as_admin_and_redirects_to_that_day()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var start = At(London, 10, 0);

        var response = await PostAsync(client, Fields(_a, start, anyStaff: true));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{ListPath}?date={Iso(_day)}");

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var row = await db.Appointments.AsNoTracking().SingleAsync();
        row.ClientId.ShouldBe(_a.ClientId);
        row.StaffMemberId.ShouldBe(_a.StaffId);
        row.ServiceId.ShouldBe(_a.ServiceId);
        row.Start.ShouldBe(start);
        row.End.ShouldBe(start.Plus(Duration.FromMinutes(30)));
        row.Status.ShouldBe(AppointmentStatus.Booked);
        row.Source.ShouldBe(AppointmentSource.Admin);
        var owner = await db.Users.AsNoTracking().SingleAsync(u => u.Role == Role.Owner);
        row.CreatedByUserId.ShouldBe(owner.Id);
        (await db.AppointmentStatusChanges.CountAsync()).ShouldBe(1);
        await using var platformScope = factory.CreateTenantScope(null);
        (await platformScope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().Select(m => m.EventType).ToListAsync()).ShouldBe([typeof(AppointmentBooked).FullName]);

        var day = await GetAsync(client, response.Headers.Location.OriginalString);
        day.ShouldContain("data-testid=\"flash\"");
        day.ShouldContain("Olivia Brown");
        day.ShouldContain("10:00");
    }

    [Fact]
    public async Task A_slot_taken_meanwhile_re_renders_with_the_message_and_fresh_slots()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var start = At(London, 10, 0);
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, FormPath));
        await BookDirectlyAsync(_studio.Id, _a, start);

        var response = await PostAsync(client, Fields(_a, start, anyStaff: false), token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("data-testid=\"error\"");
        html.ShouldContain("That time has just been taken. Pick another one below.");
        html.ShouldContain("Olivia Brown");
        var offered = SlotStarts(html);
        offered.ShouldNotBeEmpty();
        offered.ShouldNotContain(start);
        offered.ShouldBe((await SlotsAsync(_studio.Id, _a, _a.StaffId)).Select(s => s.Start).ToList());
        (await CountAsync(_studio.Id)).ShouldBe(1);

        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var polishStart = At(Warsaw, 10, 0);
        var polishToken = IdentityTestClient.AntiForgeryToken(await GetAsync(polish, FormPath));
        await BookDirectlyAsync(_polish.Id, _b, polishStart);
        var refused = await PostAsync(polish, Fields(_b, polishStart, anyStaff: true), polishToken);
        refused.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await refused.Content.ReadAsStringAsync()).ShouldContain("Ta godzina została właśnie zajęta. Wybierz inną poniżej.");
    }

    [Fact]
    public async Task Missing_or_malformed_fields_re_render_with_messages_in_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var fields = Fields(_a, At(London, 10, 0), anyStaff: true);
        fields["ClientId"] = string.Empty;
        fields["Start"] = "tomorrow-ish";

        var response = await PostAsync(client, fields);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.ShouldContain("Choose a client first.");
        html.ShouldContain("Pick one of the offered times.");
        (await CountAsync(_studio.Id)).ShouldBe(0);

        var unknownClient = Fields(_a, At(London, 10, 0), anyStaff: true);
        unknownClient["ClientId"] = _b.ClientId.ToString();
        var other = await PostAsync(client, unknownClient);
        other.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.Content.ReadAsStringAsync()).ShouldContain("This client does not exist in this salon.");
        (await CountAsync(_studio.Id)).ShouldBe(0);

        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var polishFields = Fields(_b, At(Warsaw, 10, 0), anyStaff: true);
        polishFields["ServiceId"] = string.Empty;
        var polishResponse = await PostAsync(polish, polishFields);
        polishResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await polishResponse.Content.ReadAsStringAsync()).ShouldContain("Wybierz usługę.");
    }

    [Fact]
    public async Task A_receptionist_can_book_a_staff_member_is_denied_and_a_post_without_the_token_is_refused()
    {
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await PostAsync(receptionist, Fields(_a, At(London, 11, 0), anyStaff: false))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await CountAsync(_studio.Id)).ShouldBe(1);

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var denied = await staff.GetAsync(new Uri(FormPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var anonymous = factory.ClientFor(HostA);
        var challenge = await anonymous.GetAsync(new Uri(FormPath, UriKind.Relative));
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var sneaky = await owner.PostAsync(new Uri(FormPath, UriKind.Relative), new FormUrlEncodedContent(Fields(_a, At(London, 12, 0), anyStaff: true)));
        sneaky.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await CountAsync(_studio.Id)).ShouldBe(1);
    }

    private static string Iso(LocalDate day) => LocalDatePattern.Iso.Format(day);

    private static List<Instant> SlotStarts(string html)
        => SlotStartPattern().Matches(html).Select(m => InstantPattern.ExtendedIso.Parse(m.Groups["start"].Value).Value).ToList();

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs the form for its anti-forgery token (the cookie half rides on the client) and POSTs the fields with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Dictionary<string, string> fields, string? token = null)
    {
        fields["__RequestVerificationToken"] = token ?? IdentityTestClient.AntiForgeryToken(await GetAsync(client, FormPath));
        return await client.PostAsync(new Uri(FormPath, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    private Dictionary<string, string> Fields(SalonSetup salon, Instant start, bool anyStaff) => new()
    {
        ["ClientId"] = salon.ClientId.ToString(),
        ["ServiceId"] = salon.ServiceId.ToString(),
        ["StaffId"] = salon.StaffId.ToString(),
        ["Start"] = InstantPattern.ExtendedIso.Format(start),
        ["Date"] = Iso(_day),
        ["AnyStaff"] = anyStaff ? "true" : "false",
    };

    private Instant At(DateTimeZone zone, int hour, int minute) => zone.AtLeniently(_day.At(new LocalTime(hour, minute))).ToInstant();

    private async Task BookDirectlyAsync(Guid tenantId, SalonSetup salon, Instant start)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<IBookingService>().BookAsync(
            new BookAppointmentRequest(salon.ClientId, salon.StaffId, salon.ServiceId, start, AppointmentSource.Online));
    }

    private async Task<IReadOnlyList<Slot>> SlotsAsync(Guid tenantId, SalonSetup salon, Guid? staffId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IAvailabilityService>().GetSlotsAsync(salon.ServiceId, staffId, _day, _day);
    }

    private async Task<int> CountAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.CountAsync();
    }

    private async Task<SalonSetup> SeedSalonAsync(Guid tenantId, string firstName, string lastName, string phone, string currency)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var anna = new StaffMember { DisplayName = "Anna Kowalska", Colour = "#BE185D", SortOrder = 1, CreatedAt = now };
        db.StaffMembers.Add(anna);
        foreach (var day in WorkingHoursRules.Week)
        {
            db.WorkingHours.Add(new WorkingHours { StaffMemberId = anna.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
        }

        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = currency, SortOrder = 1 };
        db.Services.Add(cut);
        db.Services.Add(new Service { Name = "Retired colour", Duration = Duration.FromMinutes(60), Price = 50m, Currency = currency, IsActive = false, SortOrder = 2 });
        var client = new Client { FirstName = firstName, LastName = lastName, Phone = phone, CreatedAt = now, UpdatedAt = now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return new SalonSetup(client.Id, anna.Id, cut.Id);
    }

    /// <summary>name="Start" value="2026-11-13T10:00:00Z" as the slot forms post it.</summary>
    [GeneratedRegex("name=\"Start\"[^>]*value=\"(?<start>[^\"]+)\"")]
    private static partial Regex SlotStartPattern();

    private sealed record SalonSetup(Guid ClientId, Guid StaffId, Guid ServiceId);
}
