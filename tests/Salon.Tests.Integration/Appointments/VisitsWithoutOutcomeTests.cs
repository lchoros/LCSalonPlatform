using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Clients;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Appointments;

/// <summary>
/// Past visits nobody marked, over HTTP (ADR 0034): the list holds the visits of earlier days that still hold their
/// time, the latest first; "client came" walks a booked visit to completed through arrived, "did not come" makes it a
/// no-show and hands over to the fee preview when the policy has a fee; a visit of today, a visit with an outcome and a
/// second click change nothing; the notice on the day list and the calendar counts them; and the gates (Bookings.Manage
/// + Clients.View, tenant, anti-forgery). Visits are written straight to the tables: the past has no free slots.
/// </summary>
[Collection(SharedDatabase.Name)]
public class VisitsWithoutOutcomeTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string ListPath = "/admin/appointments/without-outcome";
    private const string DayListPath = "/admin/appointments";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "second-salon.localhost";
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    private Tenant _studio = null!;
    private Tenant _second = null!;
    private Guid _annaId;
    private Guid _tomId;
    private Guid _serviceId;
    private Guid _clientId;
    private Instant _today;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _second = await factory.CreateTenantAsync("second-salon", name: "Second Salon");
        (_clientId, _annaId, _tomId, _serviceId) = await SeedSalonAsync(_studio.Id, "Olivia", "Brown");
        _today = London.AtStartOfDay(SystemClock.Instance.GetCurrentInstant().InZone(London).Date).ToInstant();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_list_holds_the_visits_of_earlier_days_that_still_hold_their_time_the_latest_first()
    {
        var yesterday = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Booked);
        var lastWeek = await AddVisitAsync(_today - Duration.FromDays(7), AppointmentStatus.Arrived, _tomId);
        await AddVisitAsync(_today - Duration.FromDays(3), AppointmentStatus.Completed);
        await AddVisitAsync(_today - Duration.FromDays(4), AppointmentStatus.NoShow);
        await AddVisitAsync(_today - Duration.FromDays(5), AppointmentStatus.CancelledByClient);
        var today = await AddVisitAsync(_today + Duration.FromMinutes(1), AppointmentStatus.Booked);
        var tomorrow = await AddVisitAsync(_today + Duration.FromDays(1) + Duration.FromHours(10), AppointmentStatus.Confirmed);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, ListPath);

        html.ShouldContain("<html lang=\"en-GB\"");
        html.ShouldContain("Visits without an outcome");
        html.ShouldContain("data-testid=\"without-outcome-list\" data-total=\"2\"");
        Count(html, "data-testid=\"visit\"").ShouldBe(2);
        html.IndexOf(RecordPath(yesterday, "came"), StringComparison.Ordinal)
            .ShouldBeLessThan(html.IndexOf(RecordPath(lastWeek, "came"), StringComparison.Ordinal), "the latest first");
        html.ShouldContain("Olivia Brown");
        html.ShouldContain("Cut");
        html.ShouldContain("with Anna Kowalska");
        html.ShouldContain("with Tom Reed");
        html.ShouldContain("data-testid=\"status-badge\">Booked<");
        html.ShouldContain("data-testid=\"status-badge\">Arrived<");

        html.ShouldContain(RecordPath(yesterday, "did-not-come"));
        html.ShouldNotContain(RecordPath(lastWeek, "did-not-come"), Case.Sensitive, "someone who arrived cannot be a no-show");
        html.ShouldNotContain(today.ToString());
        html.ShouldNotContain(tomorrow.ToString());
        html.ShouldContain($"href=\"/admin/appointments/calendar/{yesterday}\"");
        html.ShouldContain($"href=\"{DayListPath}\"");
        html.ShouldNotContain("data-testid=\"pager\"");
        html.ShouldNotContain("data-testid=\"empty-state\"");
    }

    [Fact]
    public async Task A_salon_with_nothing_to_mark_sees_the_empty_state()
    {
        await AddVisitAsync(_today - Duration.FromDays(1), AppointmentStatus.Completed);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(client, ListPath);

        html.ShouldContain("data-testid=\"empty-state\"");
        html.ShouldContain("Every past visit has an outcome.");
        html.ShouldNotContain("data-testid=\"without-outcome-list\"");
    }

    [Fact]
    public async Task Client_came_walks_a_booked_visit_through_arrived_to_completed_and_returns_to_the_list()
    {
        var visit = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var response = await PostAsync(client, visit, "came");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(ListPath);
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Completed);
        var history = await HistoryAsync(visit);
        history.Select(h => h.To).ShouldBe([AppointmentStatus.Booked, AppointmentStatus.Arrived, AppointmentStatus.Completed]);
        history.Skip(1).ShouldAllBe(h => h.ByUserId != null, "the person at the desk is on both steps");
        var list = await GetAsync(client, ListPath);
        list.ShouldContain("data-testid=\"flash\"");
        list.ShouldContain("The visit is marked as completed.");
        list.ShouldContain("data-testid=\"empty-state\"");
    }

    [Fact]
    public async Task Client_came_completes_a_visit_that_was_marked_as_arrived_in_one_step()
    {
        var visit = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Arrived);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        (await PostAsync(client, visit, "came")).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Completed);
        (await HistoryAsync(visit)).Select(h => h.To).ShouldBe([AppointmentStatus.Arrived, AppointmentStatus.Completed]);
    }

    [Fact]
    public async Task Did_not_come_makes_a_no_show_and_returns_to_the_list_when_the_salon_has_no_fee()
    {
        var visit = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Confirmed);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, visit, "did-not-come");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(ListPath);
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.NoShow);
        (await GetAsync(client, ListPath)).ShouldContain("The visit is marked as a no-show.");
    }

    [Fact]
    public async Task Did_not_come_hands_over_to_the_fee_preview_when_the_policy_has_a_no_show_fee()
    {
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings { NoShowFeePercent = 50m });
        }

        var visit = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var response = await PostAsync(client, visit, "did-not-come");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/admin/payments/fees/{visit}");
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.NoShow);
    }

    [Fact]
    public async Task Nothing_changes_for_a_visit_of_today_a_visit_with_an_outcome_a_second_click_or_an_outcome_that_does_not_fit()
    {
        var today = await AddVisitAsync(_today + Duration.FromMinutes(1), AppointmentStatus.Booked);
        var done = await AddVisitAsync(_today - Duration.FromDays(2), AppointmentStatus.NoShow);
        var arrived = await AddVisitAsync(_today - Duration.FromDays(3), AppointmentStatus.Arrived);
        var twice = await AddVisitAsync(_today - Duration.FromDays(4), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        (await PostAsync(client, today, "came")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(today)).ShouldBe(AppointmentStatus.Booked, "today's visits belong to the day list");
        (await GetAsync(client, ListPath)).ShouldContain("Nothing was changed");

        (await PostAsync(client, done, "came")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(done)).ShouldBe(AppointmentStatus.NoShow);

        (await PostAsync(client, arrived, "did-not-come")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(arrived)).ShouldBe(AppointmentStatus.Arrived);

        (await PostAsync(client, twice, "did-not-come")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await PostAsync(client, twice, "came")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StatusAsync(twice)).ShouldBe(AppointmentStatus.NoShow, "the first click stands");
        (await HistoryAsync(twice)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_long_list_is_paged_and_an_outcome_returns_to_the_page_it_was_recorded_on()
    {
        var visits = new List<Guid>();
        for (var i = 1; i <= 21; i++)
        {
            visits.Add(await AddVisitAsync(_today - Duration.FromDays(i) - Duration.FromHours(10), AppointmentStatus.Booked));
        }

        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var first = await GetAsync(client, ListPath);
        Count(first, "data-testid=\"visit\"").ShouldBe(20);
        first.ShouldContain("data-testid=\"pager\" data-page=\"1\" data-pages=\"2\"");
        first.ShouldContain($"href=\"{ListPath}?page=2\"");
        first.ShouldContain(RecordPath(visits[0], "came"));
        first.ShouldNotContain(visits[20].ToString());

        var second = await GetAsync(client, $"{ListPath}?page=2");
        Count(second, "data-testid=\"visit\"").ShouldBe(1);
        second.ShouldContain($"{ListPath}/{visits[20]}/came?page=2");
        (await GetAsync(client, $"{ListPath}?page=99")).ShouldContain("data-page=\"2\"", Case.Sensitive, "a page that does not exist is the last one");

        var response = await PostAsync(client, visits[20], "came", page: 2);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"{ListPath}?page=2");
        var after = await GetAsync(client, $"{ListPath}?page=2");
        after.ShouldContain("data-total=\"20\"");
        after.ShouldNotContain("data-testid=\"pager\"", Case.Sensitive, "twenty fit on one page");
    }

    [Fact]
    public async Task The_day_list_and_the_calendar_say_how_many_earlier_visits_have_no_outcome()
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await GetAsync(client, DayListPath)).ShouldNotContain("data-testid=\"without-outcome-notice\"");

        var one = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Booked);
        var single = await GetAsync(client, DayListPath);
        single.ShouldContain("data-testid=\"without-outcome-notice\" data-count=\"1\"");
        single.ShouldContain("1 earlier visit has no outcome yet.");
        single.ShouldContain($"href=\"{ListPath}\"");

        await AddVisitAsync(_today - Duration.FromDays(2), AppointmentStatus.Confirmed);
        await AddVisitAsync(_today + Duration.FromMinutes(1), AppointmentStatus.Booked);
        var calendar = await GetAsync(client, "/admin/appointments/calendar");
        calendar.ShouldContain("data-testid=\"without-outcome-notice\" data-count=\"2\"");
        calendar.ShouldContain("2 earlier visits have no outcome yet.");

        (await PostAsync(client, one, "came")).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(client, DayListPath)).ShouldContain("data-count=\"1\"");
    }

    [Fact]
    public async Task Another_salon_neither_sees_nor_marks_these_visits_and_the_gates_hold()
    {
        var visit = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Booked);
        await SeedSalonAsync(_second.Id, "Peter", "Other");

        using var other = await factory.SignedInClientAsync(HostB, _second.Id, Role.Owner);
        var theirs = await GetAsync(other, ListPath);
        theirs.ShouldContain("data-testid=\"empty-state\"");
        theirs.ShouldNotContain("Olivia Brown");
        (await GetAsync(other, DayListPath)).ShouldNotContain("data-testid=\"without-outcome-notice\"");
        (await PostAsync(other, visit, "came")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Booked);

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var denied = await staff.GetAsync(new Uri(ListPath, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        using var anonymous = factory.ClientFor(HostA);
        var challenge = await anonymous.GetAsync(new Uri(ListPath, UriKind.Relative));
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");

        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri(ListPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_or_with_an_unknown_outcome_is_refused()
    {
        var visit = await AddVisitAsync(_today - Duration.FromHours(10), AppointmentStatus.Booked);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var bare = await client.PostAsync(new Uri($"{ListPath}/{visit}/came", UriKind.Relative), new FormUrlEncodedContent([]));
        bare.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await PostAsync(client, visit, "cancel")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(client, Guid.NewGuid(), "came")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Booked);
    }

    private static string RecordPath(Guid visit, string outcome) => $"action=\"{ListPath}/{visit}/{outcome}\"";

    private static int Count(string html, string needle) => html.Split(needle).Length - 1;

    /// <summary>The page as text: entities decoded, so names and the en dash read like the screen.</summary>
    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    /// <summary>GETs the list for its anti-forgery token (the cookie half rides on the client) and POSTs the outcome with it.</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, Guid visit, string outcome, int? page = null)
    {
        var token = IdentityTestClient.AntiForgeryToken(await GetAsync(client, DayListPath));
        return await client.PostAsync(
            new Uri($"{ListPath}/{visit}/{outcome}{(page is { } number ? $"?page={number}" : string.Empty)}", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));
    }

    private async Task<AppointmentStatus> StatusAsync(Guid visit)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.Id == visit).Select(a => a.Status).SingleAsync();
    }

    private async Task<List<AppointmentStatusChange>> HistoryAsync(Guid visit)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var rows = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().AppointmentStatusChanges.AsNoTracking().Where(h => h.AppointmentId == visit).ToListAsync();
        // Two steps of one request share a moment; the status order breaks the tie.
        return [.. rows.OrderBy(h => h.At).ThenBy(h => h.To)];
    }

    /// <summary>A 30-minute visit of the seeded client, written straight to the tables with one history row for its status.</summary>
    private async Task<Guid> AddVisitAsync(Instant start, AppointmentStatus status, Guid? staffId = null)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var appointment = new Appointment
        {
            TenantId = _studio.Id,
            ClientId = _clientId,
            StaffMemberId = staffId ?? _annaId,
            ServiceId = _serviceId,
            Start = start,
            End = start + Duration.FromMinutes(30),
            Status = status,
            Source = AppointmentSource.Admin,
            PriceAmount = 20m,
            Currency = "GBP",
            DurationMinutes = 30,
            CreatedAt = start - Duration.FromDays(1),
        };
        db.Appointments.Add(appointment);
        db.AppointmentStatusChanges.Add(new AppointmentStatusChange { TenantId = _studio.Id, AppointmentId = appointment.Id, To = status, At = appointment.CreatedAt });
        await db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<(Guid ClientId, Guid AnnaId, Guid TomId, Guid ServiceId)> SeedSalonAsync(Guid tenantId, string firstName, string lastName)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var now = SystemClock.Instance.GetCurrentInstant();
        var anna = new StaffMember { DisplayName = "Anna Kowalska", Colour = "#BE185D", SortOrder = 1, CreatedAt = now };
        var tom = new StaffMember { DisplayName = "Tom Reed", Colour = "#1D4ED8", SortOrder = 2, CreatedAt = now };
        db.StaffMembers.AddRange(anna, tom);
        var cut = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
        db.Services.Add(cut);
        var client = new Client { FirstName = firstName, LastName = lastName, Phone = "+447700900123", CreatedAt = now, UpdatedAt = now };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return (client.Id, anna.Id, tom.Id, cut.Id);
    }
}