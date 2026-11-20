using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Domain;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Reports;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Dashboard;

/// <summary>
/// The admin landing page, today at a glance (ADR 0084): the visits that start today in the salon's zone by what
/// became of them, who is next, how full each person's day is and the money of the day. The numbers are the till's
/// and the report's own; a role sees only the parts it may open; nothing of another salon shows. The seeded day is
/// built around the real clock (the host has no other), so the "next" visits started a few minutes ago and the rest
/// sits in the first hours of the salon's day: only the first minutes after local midnight could read differently.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DashboardTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string DashboardPath = "/admin";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;
    private static readonly Instant DayStart = London.AtStartOfDay(Today).ToInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_numbers_of_today_match_the_day_that_was_seeded()
    {
        var day = await SeedDayAsync();

        var glance = await GlanceAsync(_studio.Id, new TodayRequest(ClientNames: true, Money: true));

        glance.Day.ShouldBe(Today);
        glance.Visits.ShouldBe(new TodayVisits(Total: 9, Booked: 1, Confirmed: 1, Arrived: 1, Completed: 3, NoShows: 1, Cancelled: 2));
        glance.StillToCome.ShouldBe(3);
        glance.Next.Select(v => (v.Id, v.ClientFirstName, v.ServiceName, v.StaffName, v.StaffColour, v.Status)).ShouldBe(
        [
            (day.Arrived, "Olivia", "Cut", "Cara", "#16A34A", AppointmentStatus.Arrived),
            (day.Booked, "Noah", "Cut", "Anna", "#7C3AED", AppointmentStatus.Booked),
            (day.Confirmed, "Olivia", "Colour", "Ben", "#0EA5E9", AppointmentStatus.Confirmed),
        ]);
        // To the minute: the database keeps microseconds, the clock gave finer ticks.
        glance.Next[0].Start.ToString("HH:mm", CultureInfo.InvariantCulture).ShouldBe(Clock(Now - Duration.FromMinutes(3)));
        glance.Next[0].End.ToString("HH:mm", CultureInfo.InvariantCulture).ShouldBe(Clock(Now + Duration.FromMinutes(27)));

        // Anna works 9 to 5 (480): a cut (30), the colour (90) and the visit in the chair (30); the no-show does not count.
        // Ben works 10 to 2 less an hour off (180): a cut (30) and the colour to come (90); the cancellations do not count.
        // Cara is not in today but has somebody in her chair. Dora is inactive and Zelda takes no bookings: neither is listed.
        glance.Staff.ShouldBe(
        [
            new StaffOccupancy(day.Anna, "Anna", "#7C3AED", 150, 480, 31.3m),
            new StaffOccupancy(day.Ben, "Ben", "#0EA5E9", 120, 180, 66.7m),
            new StaffOccupancy(day.Cara, "Cara", "#16A34A", 30, 0, null),
        ]);
        glance.BookedMinutes.ShouldBe(270, "Cara has no working time to measure against");
        glance.WorkingMinutes.ShouldBe(660);

        // The money is the till's: a cut paid in cash and a deposit by card. Two completed visits are not paid.
        var till = await ScopedAsync<ICashUpService, CashDay>(_studio.Id, s => s.GetDayAsync(null));
        var money = glance.Money.ShouldNotBeNull();
        money.Takings.ShouldBe(new Money(35m, "GBP"));
        money.Takings.ShouldBe(till.Takings);
        money.ByMethod.ShouldBe(till.Totals);
        money.ByMethod.Select(m => (m.Method, m.Amount.Amount)).ShouldBe([(PaymentMethod.Cash, 20m), (PaymentMethod.CardAtSalonTerminal, 15m)]);
        money.UnpaidCount.ShouldBe(2);
        money.UnpaidTotal.ShouldBe(new Money(65m, "GBP"), "20 + (60 - 15)");
        money.Unpaid.Select(u => (u.AppointmentId, u.ClientFirstName, u.ServiceName, u.Outstanding.Amount)).ShouldBe(
        [
            (day.Unpaid, "Noah", "Cut", 20m),
            (day.WithDeposit, "Olivia", "Colour", 45m),
        ]);

        // The report of the same day names the same unpaid visits for the same money.
        var report = await ScopedAsync<IReportService, SalonReport>(_studio.Id, s => s.GetAsync(null, Today, Today));
        report.UnpaidCount.ShouldBe(money.UnpaidCount);
        report.UnpaidTotal.ShouldBe(money.UnpaidTotal);
        report.Takings.Total.ShouldBe(money.Takings);
        report.Visits.Total.ShouldBe(glance.Visits.Total);
    }

    [Fact]
    public async Task What_was_not_asked_for_is_not_read()
    {
        await SeedDayAsync();

        var glance = await GlanceAsync(_studio.Id, new TodayRequest(ClientNames: false, Money: false));

        glance.Money.ShouldBeNull();
        glance.Next.Count.ShouldBe(3);
        glance.Next.ShouldAllBe(v => v.ClientFirstName.Length == 0);
        glance.Visits.Total.ShouldBe(9);

        var withMoney = await GlanceAsync(_studio.Id, new TodayRequest(ClientNames: false, Money: true));
        withMoney.Money.ShouldNotBeNull().Unpaid.ShouldAllBe(u => u.ClientFirstName.Length == 0);
    }

    [Fact]
    public async Task Only_the_next_five_are_named_and_a_visit_that_has_ended_is_not_next()
    {
        var (_, cutId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        var clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown");
        var people = new List<Guid>();
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            for (var i = 1; i <= 7; i++)
            {
                var person = new StaffMember { DisplayName = "Person " + i.ToString(CultureInfo.InvariantCulture), SortOrder = i, CreatedAt = Now };
                db.StaffMembers.Add(person);
                people.Add(person.Id);
            }

            await db.SaveChangesAsync();
        }

        // Six started within the last minutes and are still on; the seventh ended ten minutes ago and nobody marked it.
        var started = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            started.Add(await VisitSeed.AppointmentAsync(factory, _studio.Id, clientId, people[i], cutId, Now - Duration.FromMinutes(6 - i), AppointmentStatus.Booked));
        }

        await VisitSeed.AppointmentAsync(factory, _studio.Id, clientId, people[6], cutId, Now - Duration.FromMinutes(40), AppointmentStatus.Booked);

        var glance = await GlanceAsync(_studio.Id, new TodayRequest(ClientNames: true, Money: false));

        glance.Next.Select(v => v.Id).ShouldBe(started.Take(TodayGlance.MaxNext), "soonest first");
        glance.StillToCome.ShouldBe(6);
        glance.Visits.Booked.ShouldBe(7, "the unmarked one is still booked");

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, DashboardPath);
        Regex.Count(page, "data-testid=\"next-visit\"").ShouldBe(5);
        page.ShouldContain("data-testid=\"next-more\"");
        page.ShouldContain("1 more today");
    }

    [Fact]
    public async Task An_owner_sees_the_visits_the_money_and_the_occupancy_of_today()
    {
        var day = await SeedDayAsync();
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(owner, DashboardPath);

        page.ShouldContain("<html lang=\"en-GB\"");
        page.ShouldContain($"data-testid=\"dashboard\" data-day=\"{Iso(Today)}\"");
        page.ShouldContain("<h1 class=\"page-title\">Today</h1>");
        page.ShouldContain("data-testid=\"headline-visits\">9<");
        page.ShouldContain("data-testid=\"headline-to-come\">3<");
        page.ShouldContain("data-testid=\"headline-occupancy\">40.9%<");
        page.ShouldContain("data-testid=\"headline-takings\">£35.00<");
        page.ShouldContain("data-testid=\"status-Booked\">1<");
        page.ShouldContain("data-testid=\"status-Confirmed\">1<");
        page.ShouldContain("data-testid=\"status-Arrived\">1<");
        page.ShouldContain("data-testid=\"status-Completed\">3<");
        page.ShouldContain("data-testid=\"status-NoShow\">1<");
        page.ShouldContain("data-testid=\"status-Cancelled\">2<");

        // The next visits: time, first name, service, the person with their colour, and the way to the card.
        Regex.Count(page, "data-testid=\"next-visit\"").ShouldBe(3);
        page.ShouldContain($"data-testid=\"next-visit\" data-visit-id=\"{day.Arrived}\" data-status=\"Arrived\"");
        page.ShouldContain($"href=\"/admin/appointments/calendar/{day.Arrived}\" data-testid=\"next-visit-link\"");
        page.ShouldContain($"href=\"/admin/appointments/calendar/{day.Booked}\" data-testid=\"next-visit-link\"");
        page.ShouldContain($"data-testid=\"next-visit-time\">{Clock(Now - Duration.FromMinutes(3))}<");
        page.ShouldContain("data-testid=\"next-visit-client\">Olivia<");
        page.ShouldContain("data-testid=\"next-visit-client\">Noah<");
        page.ShouldContain("data-testid=\"next-visit-service\">Colour<");
        page.ShouldContain("data-testid=\"next-visit-staff\">Cara<");
        page.ShouldContain("background-color: #16A34A");
        page.IndexOf(day.Arrived.ToString(), StringComparison.Ordinal).ShouldBeLessThan(page.IndexOf(day.Booked.ToString(), StringComparison.Ordinal), "soonest first");
        page.ShouldNotContain("data-testid=\"next-more\"");

        // The money of the day, as the till has it, and the completed visits nobody took payment for.
        page.ShouldContain("data-testid=\"money\"");
        page.ShouldContain("data-testid=\"takings-total\">£35.00<");
        page.ShouldContain("data-testid=\"method-Cash\">£20.00<");
        page.ShouldContain("data-testid=\"method-CardAtSalonTerminal\">£15.00<");
        page.ShouldContain("Completed visits not paid: 2. Left to pay: £65.00.");
        Regex.Count(page, "data-testid=\"unpaid-visit\"").ShouldBe(2);
        page.ShouldContain($"href=\"/admin/payments/checkout/{day.Unpaid}\" data-testid=\"unpaid-checkout\"");
        page.ShouldContain($"href=\"/admin/payments/checkout/{day.WithDeposit}\" data-testid=\"unpaid-checkout\"");
        page.ShouldContain("data-testid=\"unpaid-amount\">£45.00<");
        page.ShouldContain($"href=\"/admin/reports?from={Iso(Today)}&to={Iso(Today)}\" data-testid=\"unpaid-reports\"");
        page.ShouldContain("href=\"/admin/payments/cash-up\" data-testid=\"open-till\"");

        // Occupancy: booked time over today's rota, as a bar that never runs past its end.
        Regex.Count(page, "data-testid=\"occupancy-row\"").ShouldBe(3);
        page.ShouldContain("data-testid=\"occupancy-percent\">31.3%<");
        page.ShouldContain("data-testid=\"occupancy-percent\">66.7%<");
        page.ShouldContain("role=\"progressbar\" aria-label=\"Occupancy: Anna\" aria-valuemin=\"0\" aria-valuemax=\"100\" aria-valuenow=\"31.3\"");
        page.ShouldContain("style=\"width: 31.3%\"");
        page.ShouldContain("2.5 of 8 hours booked");
        page.ShouldContain("2 of 3 hours booked");
        page.ShouldContain("Not in today. Hours booked: 0.5.");
        page.ShouldNotContain("Dora");
        page.ShouldNotContain("Zelda");

        // Quick actions, each to a page this role may open.
        page.ShouldContain("href=\"/admin/appointments/new\" data-testid=\"action-book\"");
        page.ShouldContain("href=\"/admin/appointments/calendar\" data-testid=\"action-calendar\"");
        page.ShouldContain("href=\"/admin/appointments/waitlist\" data-testid=\"action-waitlist\"");
        page.ShouldContain("href=\"/admin/payments/cash-up\" data-testid=\"action-till\"");

        // Never a surname or a way to reach the client on this page.
        page.ShouldNotContain("Brown");
        page.ShouldNotContain("Smith");
        page.ShouldNotContain("+447700900123");
        page.ShouldNotContain("olivia@example.com");
        page.ShouldNotContain("data-testid=\"new-salon\"");
    }

    [Fact]
    public async Task A_receptionist_sees_the_visits_and_the_till_money_but_no_way_into_the_reports()
    {
        var day = await SeedDayAsync();
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var page = await GetAsync(receptionist, DashboardPath);

        page.ShouldContain("data-testid=\"headline-visits\">9<");
        page.ShouldContain("data-testid=\"next-visit-client\">Olivia<");
        page.ShouldContain($"href=\"/admin/appointments/calendar/{day.Booked}\" data-testid=\"next-visit-link\"");
        page.ShouldContain("data-testid=\"takings-total\">£35.00<");
        page.ShouldContain($"href=\"/admin/payments/checkout/{day.Unpaid}\" data-testid=\"unpaid-checkout\"");
        page.ShouldContain("data-testid=\"action-book\"");
        page.ShouldContain("data-testid=\"action-till\"");
        page.ShouldNotContain("data-testid=\"unpaid-reports\"", Case.Sensitive, "a receptionist may not open the reports");
        page.ShouldNotContain("/admin/reports");
        page.ShouldNotContain("data-testid=\"dashboard-setup\"");
    }

    [Fact]
    public async Task A_role_without_the_till_sees_no_takings_and_a_role_without_clients_no_names_or_cards()
    {
        var day = await SeedDayAsync();
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);

        var page = await GetAsync(staff, DashboardPath);

        // The visits part is theirs: the counts, who is next with whom, and how full the day is.
        page.ShouldContain("data-testid=\"headline-visits\">9<");
        page.ShouldContain("data-testid=\"headline-to-come\">3<");
        page.ShouldContain("data-testid=\"headline-completed\">3<");
        page.ShouldContain("data-testid=\"status-Cancelled\">2<");
        Regex.Count(page, "data-testid=\"next-visit\"").ShouldBe(3);
        page.ShouldContain("data-testid=\"next-visit-service\">Colour<");
        page.ShouldContain("data-testid=\"next-visit-staff\">Anna<");
        Regex.Count(page, "data-testid=\"occupancy-row\"").ShouldBe(3);

        // No money in any form.
        page.ShouldNotContain("data-testid=\"money\"");
        page.ShouldNotContain("data-testid=\"headline-takings\"");
        page.ShouldNotContain("data-testid=\"takings-total\"");
        page.ShouldNotContain("data-testid=\"unpaid-visit\"");
        page.ShouldNotContain("£");

        // No client and no link to a page the role may not open.
        page.ShouldNotContain("Olivia");
        page.ShouldNotContain("Noah");
        page.ShouldNotContain("data-testid=\"next-visit-client\"");
        page.ShouldNotContain($"/admin/appointments/calendar/{day.Booked}");
        page.ShouldNotContain("data-testid=\"quick-actions\"");
        page.ShouldNotContain("data-testid=\"action-book\"");
        page.ShouldNotContain("data-testid=\"action-till\"");
        page.ShouldNotContain("data-testid=\"without-outcome-notice\"");
        page.ShouldNotContain("/admin/payments/");
        page.ShouldNotContain("/admin/reports");
    }

    [Fact]
    public async Task Nothing_of_another_salon_shows_and_nobody_gets_in_without_signing_in()
    {
        await SeedDayAsync();
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var foreign = await GetAsync(other, DashboardPath);

        foreign.ShouldNotContain("Olivia");
        foreign.ShouldNotContain("Anna");
        foreign.ShouldNotContain("data-testid=\"next-visit\"");
        foreign.ShouldNotContain("data-testid=\"occupancy-row\"");
        foreign.ShouldNotContain("data-testid=\"unpaid-visit\"");
        var glance = await GlanceAsync(_polish.Id, new TodayRequest(ClientNames: true, Money: true));
        glance.Visits.ShouldBe(new TodayVisits(0, 0, 0, 0, 0, 0, 0));
        glance.Next.ShouldBeEmpty();
        glance.Staff.ShouldBeEmpty();
        glance.Money.ShouldNotBeNull().Takings.ShouldBe(new Money(0m, "PLN"));
        glance.Money.ByMethod.ShouldBeEmpty();
        glance.Money.UnpaidCount.ShouldBe(0);

        // A browser that is not signed in is sent to the login page and comes back here.
        using var visitor = factory.ClientFor(HostA);
        var challenge = await visitor.GetAsync(new Uri(DashboardPath, UriKind.Relative));
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        challenge.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        challenge.Headers.Location.Query.ShouldContain("returnUrl=%2Fadmin");

        // Nobody signed in gets nothing; the platform's own host has no admin.
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{HostA}") });
        (await anonymous.GetAsync(new Uri(DashboardPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var platform = factory.ClientFor("localhost");
        (await platform.GetAsync(new Uri(DashboardPath, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_brand_new_salon_gets_an_honest_empty_state_that_points_the_owner_at_getting_started()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(owner, DashboardPath);

        page.ShouldContain("data-testid=\"new-salon\"");
        page.ShouldContain("Nothing to show yet");
        page.ShouldContain("data-testid=\"dashboard-setup\" data-done=\"1\" data-required=\"8\"");
        page.ShouldContain("1 of 8 steps done");
        page.ShouldContain("href=\"/admin/settings/getting-started\" data-testid=\"dashboard-setup-link\"");
        page.ShouldNotContain("data-testid=\"headline-visits\"", Case.Sensitive, "no row of zeros for a salon that cannot take a booking yet");
        page.ShouldNotContain("data-testid=\"money\"");
        page.ShouldNotContain("data-testid=\"occupancy\"");
        page.ShouldNotContain("data-testid=\"quick-actions\"");

        // A manager runs the desk but could not open the getting started page: the same honesty, no link.
        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager, "manager@studio-20.test");
        var managers = await GetAsync(manager, DashboardPath);
        managers.ShouldContain("data-testid=\"new-salon\"");
        managers.ShouldNotContain("data-testid=\"dashboard-setup\"");
        managers.ShouldNotContain("/admin/settings/getting-started");
    }

    [Fact]
    public async Task A_quiet_day_says_so_and_offers_to_book()
    {
        await VisitSeed.SalonAsync(factory, _studio.Id);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(owner, DashboardPath);

        page.ShouldNotContain("data-testid=\"new-salon\"", Case.Sensitive, "somebody takes bookings");
        page.ShouldContain("data-testid=\"headline-visits\">0<");
        page.ShouldContain("data-testid=\"headline-occupancy\">No data yet<");
        page.ShouldContain("data-testid=\"no-next-visits\"");
        page.ShouldContain("Nothing is booked for today yet.");
        page.ShouldContain("data-testid=\"takings-total\">£0.00<");
        page.ShouldContain("data-testid=\"no-unpaid\"");
        page.ShouldContain("data-testid=\"action-book\"");

        // Set-up is still unfinished, and the owner is told where to carry on.
        page.ShouldContain("data-testid=\"dashboard-setup\"");
        page.ShouldContain("href=\"/admin/settings/getting-started\" data-testid=\"dashboard-setup-link\"");
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var page = await GetAsync(owner, DashboardPath);

        page.ShouldContain("<html lang=\"pl-PL\"");
        page.ShouldContain("<h1 class=\"page-title\">Dzisiaj</h1>");
        page.ShouldContain("data-testid=\"nav-dashboard\" aria-current=\"true\" class=\"nav-link\">Pulpit<");
        page.ShouldContain("Nie ma jeszcze czego pokazać");
    }

    /// <summary>
    /// Today in London. Completed: a cut by Anna paid in cash (£20), a cut by Ben nobody paid for, a 90-minute colour
    /// (£60) by Anna with a £15 card deposit held. A no-show with Anna, a cancellation by the client and one by the
    /// salon with Ben. Still on: Olivia arrived for a cut with Cara three minutes ago, Noah is booked with Anna two
    /// minutes ago, Olivia is confirmed for a colour with Ben a minute ago. One visit yesterday and one tomorrow, both
    /// outside. Anna works every day 9 to 5; Ben 10 to 2 and is away from 10 to 11 today; Cara has no rota; Dora has
    /// one but is inactive; Zelda works but takes no bookings.
    /// </summary>
    private async Task<SeededDay> SeedDayAsync()
    {
        Guid anna, ben, cara, cut, colour;
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var annaRow = new StaffMember { DisplayName = "Anna", Colour = "#7C3AED", SortOrder = 0, CreatedAt = Now };
            var benRow = new StaffMember { DisplayName = "Ben", Colour = "#0EA5E9", SortOrder = 1, CreatedAt = Now };
            var caraRow = new StaffMember { DisplayName = "Cara", Colour = "#16A34A", SortOrder = 2, CreatedAt = Now };
            var dora = new StaffMember { DisplayName = "Dora", SortOrder = 3, IsActive = false, CreatedAt = Now };
            var eve = new StaffMember { DisplayName = "Zelda", SortOrder = 4, IsBookable = false, CreatedAt = Now };
            var cutRow = new Service { Name = "Cut", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };
            var colourRow = new Service { Name = "Colour", Duration = Duration.FromMinutes(90), Price = 60m, Currency = "GBP" };
            db.StaffMembers.AddRange(annaRow, benRow, caraRow, dora, eve);
            db.Services.AddRange(cutRow, colourRow);
            foreach (var weekday in Enum.GetValues<IsoDayOfWeek>().Where(d => d != IsoDayOfWeek.None))
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = annaRow.Id, Day = weekday, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = benRow.Id, Day = weekday, Start = new LocalTime(10, 0), End = new LocalTime(14, 0) });
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = dora.Id, Day = weekday, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = eve.Id, Day = weekday, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
            }

            db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = benRow.Id, FromDate = Today, ToDate = Today, Start = new LocalTime(10, 0), End = new LocalTime(11, 0), CreatedAt = Now });
            await db.SaveChangesAsync();
            (anna, ben, cara, cut, colour) = (annaRow.Id, benRow.Id, caraRow.Id, cutRow.Id, colourRow.Id);
        }

        var olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123", "olivia@example.com");
        var noah = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Smith");

        // The colour holds its time while the deposit is recorded, so it goes in first and is completed at once.
        var withDeposit = await VisitAsync(olivia, anna, colour, DayStart + Duration.FromMinutes(40), AppointmentStatus.Booked, 90, 60m);
        await AccountAsync(s => s.RecordDepositAsync(withDeposit, 15m, PaymentMethod.CardAtSalonTerminal, null));
        await SetAsync(withDeposit, AppointmentStatus.Completed);
        var paid = await VisitAsync(olivia, anna, cut, DayStart + Duration.FromMinutes(5), AppointmentStatus.Completed);
        await AccountAsync(s => s.PayBillAsync(paid, PaymentMethod.Cash, [], null));
        var unpaid = await VisitAsync(noah, ben, cut, DayStart + Duration.FromMinutes(5), AppointmentStatus.Completed);
        await VisitAsync(noah, anna, cut, DayStart + Duration.FromMinutes(135), AppointmentStatus.NoShow);
        await VisitAsync(noah, ben, cut, DayStart + Duration.FromMinutes(40), AppointmentStatus.CancelledByClient);
        await VisitAsync(noah, ben, cut, DayStart + Duration.FromMinutes(75), AppointmentStatus.CancelledBySalon);

        // Outside today on both sides.
        await VisitAsync(olivia, anna, cut, DayStart - Duration.FromMinutes(60), AppointmentStatus.Completed);
        await VisitAsync(olivia, cara, cut, London.AtStartOfDay(Today.PlusDays(1)).ToInstant() + Duration.FromMinutes(600), AppointmentStatus.Booked);

        var arrived = await VisitAsync(olivia, cara, cut, Now - Duration.FromMinutes(3), AppointmentStatus.Arrived);
        var booked = await VisitAsync(noah, anna, cut, Now - Duration.FromMinutes(2), AppointmentStatus.Booked);
        var confirmed = await VisitAsync(olivia, ben, colour, Now - Duration.FromMinutes(1), AppointmentStatus.Confirmed, 90, 60m);
        return new SeededDay(anna, ben, cara, arrived, booked, confirmed, unpaid, withDeposit);
    }

    private Task<Guid> VisitAsync(Guid clientId, Guid staffId, Guid serviceId, Instant start, AppointmentStatus status, int minutes = 30, decimal price = 20m)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, clientId, staffId, serviceId, start, status, minutes, price);

    private async Task SetAsync(Guid appointmentId, AppointmentStatus status)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    private Task<TodayGlance> GlanceAsync(Guid tenantId, TodayRequest request)
        => ScopedAsync<ITodayService, TodayGlance>(tenantId, s => s.GetAsync(request));

    private Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act) => ScopedAsync(_studio.Id, act);

    private async Task<T> ScopedAsync<TService, T>(Guid tenantId, Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static LocalTime LocalTimeOf(Instant instant) => instant.InZone(London).TimeOfDay;

    private static string Clock(Instant instant) => LocalTimeOf(instant).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Iso(LocalDate day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private sealed record SeededDay(Guid Anna, Guid Ben, Guid Cara, Guid Arrived, Guid Booked, Guid Confirmed, Guid Unpaid, Guid WithDeposit);
}