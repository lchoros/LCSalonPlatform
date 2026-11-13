using System.Net;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Catalog;
using Salon.Core.Domain;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Reports;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// The reports page (ADR 0026): money adds up exactly as on the till, a visit counts on the day it starts in the
/// salon's zone, utilisation is booked time over the rota less time off, completed visits nobody took payment for are
/// named, and only an Owner or a Manager of the same salon sees any of it.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ReportsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string ReportsPath = "/admin/reports";
    private const string MarchWeek = ReportsPath + "?from=2026-03-02&to=2026-03-08";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    /// <summary>A week without a clock change, Monday to Sunday.</summary>
    private static readonly LocalDate Monday = new(2026, 3, 2);
    private static readonly LocalDate Sunday = new(2026, 3, 8);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _annaId;
    private Guid _cutId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_annaId, _cutId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Money_adds_up_exactly_as_on_the_till_and_counts_on_the_day_it_was_recorded()
    {
        await SavePolicyAsync(_studio.Id, noShowPercent: 100m);
        await RecordADayAsync();
        var till = await ScopedAsync<ICashUpService, CashDay>(_studio.Id, s => s.GetDayAsync(null));

        var report = await ReportAsync(_studio.Id, null, till.Today, till.Today);

        report.Period.ShouldBe(new ReportPeriod(till.Today, till.Today));
        report.RangeRefused.ShouldBeFalse();
        report.Takings.Total.ShouldBe(till.Takings);
        report.Takings.Total.ShouldBe(new Money(45m, "GBP"));
        report.Takings.ByMethod.ShouldBe(till.Totals);
        report.Takings.ByKind.Select(k => (k.Kind, k.Amount.Amount)).ShouldBe(
        [
            (ClientAccountEntryKind.FeePayment, 20m),
            (ClientAccountEntryKind.DepositPaid, 15m),
            (ClientAccountEntryKind.DepositReturned, -10m),
            (ClientAccountEntryKind.VisitPayment, 20m),
        ]);
        report.Takings.ByDay.ShouldBe([new DayTakings(till.Today, new Money(45m, "GBP"))]);

        // The missed visit was four days ago; its fee was added and paid today, so the fee is today's.
        report.Fees.ShouldBe(new FeesReport(new Money(20m, "GBP"), new Money(0m, "GBP"), new Money(20m, "GBP")));

        // Yesterday and the other salon saw none of it.
        var yesterday = await ReportAsync(_studio.Id, null, till.Today.PlusDays(-1), till.Today.PlusDays(-1));
        yesterday.Takings.ByMethod.ShouldBeEmpty();
        yesterday.Takings.Total.ShouldBe(new Money(0m, "GBP"));
        var other = await ReportAsync(_polish.Id, null, till.Today, till.Today);
        other.Takings.ByDay.ShouldBeEmpty();
        other.Takings.Total.ShouldBe(new Money(0m, "PLN"));
        other.Visits.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Visits_count_on_the_day_they_start_in_the_salon_zone_and_the_no_show_rate_ignores_what_had_no_outcome()
    {
        await SeedMarchWeekAsync();

        var report = await ReportAsync(_studio.Id, null, Monday, Sunday);

        report.Preset.ShouldBeNull();
        report.Visits.ShouldBe(new VisitsReport(
            Total: 7,
            Completed: 3,
            Open: 1,
            NoShows: 1,
            CancelledByClient: 1,
            CancelledBySalon: 1,
            Online: 1,
            NoShowRate: 25m,
            CompletedValue: new Money(100m, "GBP")));
        report.TopServices.ShouldBe(
        [
            new ServiceLine("Cut", 2, new Money(40m, "GBP")),
            new ServiceLine("Colour", 1, new Money(60m, "GBP")),
        ]);

        // The Sunday before and the Monday after have one visit each and nothing more.
        (await ReportAsync(_studio.Id, null, Monday.PlusDays(-1), Monday.PlusDays(-1))).Visits.Total.ShouldBe(1);
        (await ReportAsync(_studio.Id, null, Sunday.PlusDays(1), Sunday.PlusDays(1))).Visits.Total.ShouldBe(1);
        (await ReportAsync(_polish.Id, null, Monday, Sunday)).Visits.Total.ShouldBe(0);
    }

    [Fact]
    public async Task Utilisation_is_booked_time_over_the_rota_less_time_off()
    {
        await SeedMarchWeekAsync();

        var report = await ReportAsync(_studio.Id, null, Monday, Sunday);

        // Anna: Monday to Friday 9 to 5 is 2400 minutes, less a whole Wednesday (480) and two hours on Thursday (120).
        // Booked: a cut (30), the colour (90) and the visit still to come (30). The no-show and the cancellations do not count.
        // Ben has no rota but did one cut. Cara is inactive and did nothing, so she is not listed.
        report.Staff.ShouldBe(
        [
            new StaffLine("Anna", 2, new Money(80m, "GBP"), 150, 1800, 8.3m),
            new StaffLine("Ben", 1, new Money(20m, "GBP"), 30, 0, null),
        ]);

        // One Wednesday alone: Anna is away all day, so there is no working time to measure against.
        var wednesday = await ReportAsync(_studio.Id, null, Monday.PlusDays(2), Monday.PlusDays(2));
        wednesday.Staff.ShouldBeEmpty();
    }

    [Fact]
    public async Task Completed_visits_nobody_took_payment_for_are_named_with_what_is_left_after_the_deposit()
    {
        var (paid, withDeposit, plain) = await SeedMarchWeekAsync();

        var report = await ReportAsync(_studio.Id, null, Monday, Sunday);

        report.UnpaidCount.ShouldBe(3);
        report.UnpaidTotal.ShouldBe(new Money(95m, "GBP"), "20 + (20 - 5) + 60");
        report.Unpaid.Select(u => (u.AppointmentId, u.ClientName, u.ServiceName, u.Outstanding.Amount)).ShouldBe(
        [
            (paid, "Olivia Brown", "Cut", 20m),
            (withDeposit, "Olivia Brown", "Cut", 15m),
            (plain, "Olivia Brown", "Colour", 60m),
        ]);
        report.Unpaid[0].Start.LocalDateTime.ShouldBe(Monday.At(new LocalTime(10, 0)));

        // Paid at the checkout today: the visit drops off the list although the money is today's, not March's.
        await AccountAsync(s => s.PayBillAsync(paid, PaymentMethod.Cash, [], null));

        var after = await ReportAsync(_studio.Id, null, Monday, Sunday);
        after.UnpaidCount.ShouldBe(2);
        after.UnpaidTotal.ShouldBe(new Money(75m, "GBP"));
        after.Unpaid.Select(u => u.AppointmentId).ShouldBe([withDeposit, plain]);
        after.Takings.ByMethod.ShouldBeEmpty("the payment was recorded today");
    }

    [Fact]
    public async Task A_deposit_that_covers_the_price_or_was_returned_is_counted_for_what_it_is()
    {
        var covered = await VisitAsync(At(Monday, 9, 0), AppointmentStatus.Booked, _annaId, _cutId);
        await AccountAsync(s => s.RecordDepositAsync(covered, 20m, PaymentMethod.Cash, null));
        var returned = await VisitAsync(At(Monday, 11, 0), AppointmentStatus.Booked, _annaId, _cutId);
        await AccountAsync(s => s.RecordDepositAsync(returned, 5m, PaymentMethod.Cash, null));
        await AccountAsync(s => s.ReturnDepositAsync(returned, PaymentMethod.Cash, null));
        await SetAsync(covered, AppointmentStatus.Completed);
        await SetAsync(returned, AppointmentStatus.Completed);

        var report = await ReportAsync(_studio.Id, null, Monday, Sunday);

        report.Unpaid.Select(u => (u.AppointmentId, u.Outstanding.Amount)).ShouldBe([(returned, 20m)]);
    }

    [Fact]
    public async Task The_period_is_two_dates_a_preset_or_this_month()
    {
        var today = Now.InZone(London).Date;

        var byDefault = await ReportAsync(_studio.Id, null, null, null);
        byDefault.Preset.ShouldBe(ReportPreset.ThisMonth);
        byDefault.Period.ShouldBe(ReportRules.Preset(ReportPreset.ThisMonth, today));
        byDefault.Today.ShouldBe(today);

        var lastWeek = await ReportAsync(_studio.Id, ReportPreset.LastWeek, null, null);
        lastWeek.Period.ShouldBe(ReportRules.Preset(ReportPreset.LastWeek, today));

        var inverted = await ReportAsync(_studio.Id, null, Sunday, Monday);
        inverted.RangeRefused.ShouldBeTrue();
        inverted.Period.ShouldBe(byDefault.Period);
    }

    [Fact]
    public async Task The_page_shows_the_week_to_an_owner()
    {
        var (_, withDeposit, _) = await SeedMarchWeekAsync();
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(client, MarchWeek);

        page.ShouldContain("data-testid=\"nav-reports\"");
        page.ShouldContain("data-testid=\"period\" data-from=\"2026-03-02\" data-to=\"2026-03-08\"");
        page.ShouldContain("No money was recorded in this period.");
        page.ShouldContain("data-testid=\"visits-total\">7<");
        page.ShouldContain("data-testid=\"visits-completed\">3<");
        page.ShouldContain("data-testid=\"visits-open\">1<");
        page.ShouldContain("data-testid=\"visits-no-shows\">1<");
        page.ShouldContain("data-testid=\"visits-online\">1<");
        page.ShouldContain("data-testid=\"no-show-rate\">25%<");
        page.ShouldContain("data-testid=\"completed-value\">£100.00<");
        page.ShouldContain("Visits: 3. Left to pay: £95.00.");
        page.ShouldContain($"href=\"/admin/payments/checkout/{withDeposit}\" data-testid=\"unpaid-checkout\"");
        page.ShouldContain("data-testid=\"unpaid-amount\">£15.00<");
        page.ShouldContain("Colour");
        page.ShouldContain("data-testid=\"utilisation\">8.3%<");
        page.ShouldContain("value=\"8.3\"");
        page.ShouldContain("Booked 2.5 of 30 working hours");
        page.ShouldContain("No working hours in this period. Hours booked: 0.5.");
        page.ShouldNotContain("data-testid=\"fees\"", Case.Sensitive, "no fee was touched in that week");
        page.ShouldNotContain("aria-current=\"page\"", Case.Sensitive, "a custom range is none of the one-tap periods");
    }

    [Fact]
    public async Task The_page_shows_money_with_a_link_to_the_till_of_each_day()
    {
        await SavePolicyAsync(_studio.Id, noShowPercent: 100m);
        await RecordADayAsync();
        var today = Now.InZone(London).Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);

        var page = await GetAsync(client, ReportsPath);

        page.ShouldContain("data-testid=\"preset-ThisMonth\" aria-current=\"page\"");
        page.ShouldContain("data-testid=\"takings-total\">£45.00<");
        page.ShouldContain("data-testid=\"method-Cash\">£15.00<");
        page.ShouldContain("data-testid=\"method-CardAtSalonTerminal\">£20.00<");
        page.ShouldContain("data-testid=\"method-BankTransfer\">£10.00<");
        page.ShouldContain("data-testid=\"kind-DepositReturned\">−£10.00<");
        page.ShouldContain($"data-testid=\"takings-day\" data-day=\"{today}\"");
        page.ShouldContain($"href=\"/admin/payments/cash-up?date={today}\"");
        page.ShouldContain("data-testid=\"fees-added\">£20.00<");
        page.ShouldContain("data-testid=\"fees-paid\">£20.00<");

        var lastWeek = await GetAsync(client, ReportsPath + "?period=last-week");
        lastWeek.ShouldContain("data-testid=\"preset-LastWeek\" aria-current=\"page\"");
        lastWeek.ShouldNotContain("data-testid=\"preset-ThisMonth\" aria-current=\"page\"");
    }

    [Theory]
    [InlineData("?from=2026-03-08&to=2026-03-02")]
    [InlineData("?from=2024-01-01&to=2026-03-02")]
    [InlineData("?from=2026-03-02")]
    public async Task Dates_that_cannot_be_used_show_this_month_and_say_so(string query)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(client, ReportsPath + query);

        page.ShouldContain("Those dates cannot be used. Choose a first and a last day, at most 366 days apart.");
        page.ShouldContain("data-testid=\"preset-ThisMonth\" aria-current=\"page\"");
    }

    [Theory]
    [InlineData("?period=nonsense")]
    [InlineData("?from=yesterday&to=tomorrow")]
    public async Task An_address_that_cannot_be_read_shows_this_month(string query)
    {
        using var client = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var page = await GetAsync(client, ReportsPath + query);

        page.ShouldContain("data-testid=\"preset-ThisMonth\" aria-current=\"page\"");
        page.ShouldNotContain("data-testid=\"error\"");
    }

    [Fact]
    public async Task The_page_speaks_the_salon_language()
    {
        using var client = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var page = await GetAsync(client, MarchWeek);

        page.ShouldContain("Raporty");
        page.ShouldContain("data-testid=\"period\" data-from=\"2026-03-02\" data-to=\"2026-03-08\">Od ");
        page.ShouldContain("W tym okresie nie zapisano żadnych płatności.");
        page.ShouldContain("Odsetek nieobecności");
        page.ShouldContain("data-testid=\"no-show-rate\">Brak danych<");
        page.ShouldContain("data-testid=\"completed-value\">0,00 zł<");
        page.ShouldContain("Wszystkie zakończone wizyty z tego okresu są opłacone.");
    }

    [Fact]
    public async Task Only_an_owner_or_a_manager_of_the_same_salon_sees_the_numbers()
    {
        await SeedMarchWeekAsync();

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var receptionist = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        foreach (var denied in new[] { staff, receptionist })
        {
            var get = await denied.GetAsync(new Uri(MarchWeek, UriKind.Relative));
            get.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        }

        // The receptionist's menu does not offer what the role cannot open.
        (await GetAsync(receptionist, "/admin/appointments")).ShouldNotContain("data-testid=\"nav-reports\"");

        using var manager = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Manager);
        (await GetAsync(manager, MarchWeek)).ShouldContain("data-testid=\"visits-total\">7<");

        // The same week in another salon is empty: nothing of Studio 20 leaks into it.
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var foreign = await GetAsync(other, MarchWeek);
        foreign.ShouldContain("data-testid=\"visits-total\">0<");
        foreign.ShouldNotContain("Olivia Brown");
        foreign.ShouldNotContain("Anna");

        // Nobody signed in gets nothing.
        using var anonymous = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{HostA}") });
        (await anonymous.GetAsync(new Uri(MarchWeek, UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// The week of 2 March 2026 in London. Completed: a cut by Anna on Monday at 10:00, a cut by Ben on Tuesday with a £5
    /// deposit held, a 90-minute colour (£60) by Anna on Friday. A no-show, a cancellation by the client and one by the
    /// salon. An online booking on Sunday at 23:30, still booked. One visit late on the Sunday before and one at midnight
    /// on the Monday after, both outside. Anna works Monday to Friday 9 to 5, is away all Wednesday and on Thursday from
    /// 12 to 2. Ben has no rota. Cara has one but is inactive.
    /// </summary>
    private async Task<(Guid Paid, Guid WithDeposit, Guid Plain)> SeedMarchWeekAsync()
    {
        Guid benId;
        Guid colourId;
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var ben = new StaffMember { DisplayName = "Ben", SortOrder = 1, CreatedAt = Now };
            var cara = new StaffMember { DisplayName = "Cara", SortOrder = 2, IsActive = false, CreatedAt = Now };
            var colour = new Service { Name = "Colour", Duration = Duration.FromMinutes(90), Price = 60m, Currency = "GBP" };
            db.StaffMembers.AddRange(ben, cara);
            db.Services.Add(colour);
            foreach (var day in new[] { IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday })
            {
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = _annaId, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
                db.WorkingHours.Add(new WorkingHours { StaffMemberId = cara.Id, Day = day, Start = new LocalTime(9, 0), End = new LocalTime(17, 0) });
            }

            db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = _annaId, FromDate = Monday.PlusDays(2), ToDate = Monday.PlusDays(2), CreatedAt = Now });
            db.StaffTimeOff.Add(new StaffTimeOff { StaffMemberId = _annaId, FromDate = Monday.PlusDays(3), ToDate = Monday.PlusDays(3), Start = new LocalTime(12, 0), End = new LocalTime(14, 0), CreatedAt = Now });
            await db.SaveChangesAsync();
            benId = ben.Id;
            colourId = colour.Id;
        }

        var paid = await VisitAsync(At(Monday, 10, 0), AppointmentStatus.Completed, _annaId, _cutId);
        var withDeposit = await VisitAsync(At(Monday.PlusDays(1), 10, 0), AppointmentStatus.Booked, benId, _cutId);
        await AccountAsync(s => s.RecordDepositAsync(withDeposit, 5m, PaymentMethod.BankTransfer, null));
        await SetAsync(withDeposit, AppointmentStatus.Completed);
        var plain = await VisitAsync(At(Monday.PlusDays(4), 14, 0), AppointmentStatus.Completed, _annaId, colourId, minutes: 90, price: 60m);
        await VisitAsync(At(Monday, 12, 0), AppointmentStatus.NoShow, _annaId, _cutId);
        await VisitAsync(At(Monday, 13, 0), AppointmentStatus.CancelledByClient, _annaId, _cutId);
        await VisitAsync(At(Monday.PlusDays(1), 13, 0), AppointmentStatus.CancelledBySalon, _annaId, _cutId);
        var online = await VisitAsync(At(Sunday, 23, 30), AppointmentStatus.Booked, _annaId, _cutId);
        await VisitAsync(At(Monday.PlusDays(-1), 23, 30), AppointmentStatus.Completed, _annaId, _cutId);
        await VisitAsync(At(Sunday.PlusDays(1), 0, 0), AppointmentStatus.Completed, _annaId, _cutId);

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
                .Where(a => a.Id == online)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Source, AppointmentSource.Online));
        }

        return (paid, withDeposit, plain);
    }

    /// <summary>
    /// The day of the till tests: a deposit in cash (£5), a visit paid by card (£20), a deposit by transfer (£10) that
    /// goes back in cash, and a no-show fee paid in cash (£20): cash 5 - 10 + 20 = 15, card 20, transfer 10.
    /// </summary>
    private async Task RecordADayAsync()
    {
        var upcoming = await VisitAsync(Now + Duration.FromDays(2), AppointmentStatus.Booked, _annaId, _cutId);
        await AccountAsync(s => s.RecordDepositAsync(upcoming, 5m, PaymentMethod.Cash, null));
        var arrived = await VisitAsync(Now - Duration.FromMinutes(45), AppointmentStatus.Arrived, _annaId, _cutId);
        await AccountAsync(s => s.PayBillAsync(arrived, PaymentMethod.CardAtSalonTerminal, [], null));
        var cancelled = await VisitAsync(Now + Duration.FromDays(3), AppointmentStatus.Booked, _annaId, _cutId);
        await AccountAsync(s => s.RecordDepositAsync(cancelled, 10m, PaymentMethod.BankTransfer, null));
        await AccountAsync(s => s.ReturnDepositAsync(cancelled, PaymentMethod.Cash, null));
        var missed = await VisitAsync(Now - Duration.FromDays(4), AppointmentStatus.NoShow, _annaId, _cutId);
        await AccountAsync(s => s.ChargeFeeAsync(missed, null, null));
        await AccountAsync(s => s.RecordFeePaymentAsync(missed, PaymentMethod.Cash, null));
    }

    private static Instant At(LocalDate day, int hour, int minute) => day.At(new LocalTime(hour, minute)).InZoneStrictly(London).ToInstant();

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status, Guid staffId, Guid serviceId, int minutes = 30, decimal price = 20m)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, staffId, serviceId, start, status, minutes, price);

    private async Task SetAsync(Guid appointmentId, AppointmentStatus status)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    private async Task SavePolicyAsync(Guid tenantId, decimal noShowPercent)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings { NoShowFeePercent = noShowPercent });
    }

    private Task<SalonReport> ReportAsync(Guid tenantId, ReportPreset? preset, LocalDate? from, LocalDate? to)
        => ScopedAsync<IReportService, SalonReport>(tenantId, s => s.GetAsync(preset, from, to));

    private Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act) => ScopedAsync(_studio.Id, act);

    private async Task<T> ScopedAsync<TService, T>(Guid tenantId, Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}