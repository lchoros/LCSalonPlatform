using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Reports;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// The report set against the period before (ADR 0046): the same days one period earlier, counted only as far as
/// today. The period here is yesterday to tomorrow, three days of which two have happened, so it is compared with
/// the two days three and four days ago. A cut is £20.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ReportComparisonTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;
    private static readonly ReportPeriod Period = new(Today.PlusDays(-1), Today.PlusDays(1));

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_days_that_have_happened_are_set_against_the_same_days_one_period_earlier()
    {
        await SeedBothPeriodsAsync();

        var compared = (await ReportAsync(_studio.Id, Period)).Comparison.ShouldNotBeNull();

        compared.SoFar.ShouldBe(new ReportPeriod(Today.PlusDays(-1), Today), "tomorrow has not happened");
        compared.Before.ShouldBe(new ReportPeriod(Today.PlusDays(-4), Today.PlusDays(-3)));
        (compared.Takings.Amount, compared.TakingsBefore.Amount, compared.TakingsChange).ShouldBe((20m, 60m, -66.7m));
        (compared.Completed, compared.CompletedBefore, compared.CompletedChange).ShouldBe((1, 2, -50m));
        (compared.NoShows, compared.NoShowsBefore, compared.NoShowsChange).ShouldBe((0, 1, -100m));
        compared.HasAny.ShouldBeTrue();
    }

    [Fact]
    public async Task Nothing_before_gives_no_percentage_and_nothing_at_all_gives_no_comparison_to_show()
    {
        var arrived = await VisitAsync(Now - Duration.FromMinutes(45), AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(arrived, PaymentMethod.Cash, [], null));
        await SetAsync(arrived, AppointmentStatus.Completed);

        var busy = (await ReportAsync(_studio.Id, Period)).Comparison.ShouldNotBeNull();
        var empty = (await ReportAsync(_polish.Id, Period)).Comparison.ShouldNotBeNull();

        (busy.Takings.Amount, busy.TakingsBefore.Amount, busy.TakingsChange).ShouldBe((20m, 0m, null));
        (busy.Completed, busy.CompletedBefore, busy.CompletedChange).ShouldBe((1, 0, null));
        busy.HasAny.ShouldBeTrue();
        empty.HasAny.ShouldBeFalse("another salon's money and visits are not this salon's");
        empty.Takings.Currency.ShouldBe("PLN");
    }

    [Fact]
    public async Task A_period_that_has_not_begun_is_not_compared()
    {
        await SeedBothPeriodsAsync();

        var report = await ReportAsync(_studio.Id, new ReportPeriod(Today.PlusDays(1), Today.PlusDays(7)));

        report.Comparison.ShouldBeNull();
    }

    [Fact]
    public async Task The_page_shows_both_periods_side_by_side_and_says_which_days_they_are()
    {
        await SeedBothPeriodsAsync();
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var iso = LocalDatePattern.Iso;

        var html = await GetAsync(owner, $"/admin/reports?from={iso.Format(Period.From)}&to={iso.Format(Period.To)}");

        html.ShouldContain("data-testid=\"comparison\"");
        html.ShouldContain("Against the period before");
        html.ShouldContain($"data-testid=\"comparison-days\" data-before-from=\"{iso.Format(Today.PlusDays(-4))}\" data-before-to=\"{iso.Format(Today.PlusDays(-3))}\"");
        html.ShouldContain("data-testid=\"compare-takings-now\">£20.00<");
        html.ShouldContain("data-testid=\"compare-takings-before\">£60.00<");
        html.ShouldContain("data-testid=\"compare-takings-change\">Down 66.7%<");
        html.ShouldContain("data-testid=\"compare-completed-now\">1<");
        html.ShouldContain("data-testid=\"compare-completed-before\">2<");
        html.ShouldContain("data-testid=\"compare-completed-change\">Down 50%<");
        html.ShouldContain("data-testid=\"compare-no-shows-change\">Down 100%<");
    }

    [Fact]
    public async Task The_page_says_up_no_change_and_nothing_before_in_words_and_leaves_an_empty_comparison_out()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var iso = LocalDatePattern.Iso;
        var path = $"/admin/reports?from={iso.Format(Period.From)}&to={iso.Format(Period.To)}";
        (await GetAsync(owner, path)).ShouldNotContain("data-testid=\"comparison\"");

        // One completed visit in each period, money only now: visits are level, takings have nothing to be set against.
        var before = await VisitAsync(Now - Duration.FromDays(3), AppointmentStatus.Completed);
        var arrived = await VisitAsync(Now - Duration.FromMinutes(45), AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(arrived, PaymentMethod.Cash, [], null));
        await SetAsync(arrived, AppointmentStatus.Completed);
        await MoneyAsync(before, Now - Duration.FromDays(3), 10m);

        var html = await GetAsync(owner, path);

        html.ShouldContain("data-testid=\"compare-takings-change\">Up 100%<");
        html.ShouldContain("data-testid=\"compare-completed-change\">No change<");
        html.ShouldContain("data-testid=\"compare-no-shows-change\">Nothing before<");
    }

    /// <summary>
    /// Now: one visit paid today (£20). Three days ago: two completed visits, a no-show, and £60 taken.
    /// </summary>
    private async Task SeedBothPeriodsAsync()
    {
        var arrived = await VisitAsync(Now - Duration.FromMinutes(45), AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(arrived, PaymentMethod.CardAtSalonTerminal, [], null));
        await SetAsync(arrived, AppointmentStatus.Completed);
        var threeDaysAgo = Now - Duration.FromDays(3);
        var paid = await VisitAsync(threeDaysAgo, AppointmentStatus.Completed);
        await VisitAsync(threeDaysAgo - Duration.FromHours(1), AppointmentStatus.Completed);
        await VisitAsync(threeDaysAgo - Duration.FromHours(2), AppointmentStatus.NoShow);
        await MoneyAsync(paid, threeDaysAgo, 60m);
    }

    private async Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
    {
        var id = await VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, AppointmentStatus.Booked);
        await SetAsync(id, status);
        return id;
    }

    /// <summary>The service records the money; the checkout page is what completes an arrived visit, so a test of the service says so itself.</summary>
    private async Task SetAsync(Guid appointmentId, AppointmentStatus status)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments
            .Where(a => a.Id == appointmentId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    /// <summary>A payment recorded in the past, written straight to the ledger: the services only ever record "now".</summary>
    private async Task MoneyAsync(Guid visit, Instant at, decimal amount)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.ClientAccountEntries.Add(new ClientAccountEntry
        {
            ClientId = _clientId,
            AppointmentId = visit,
            Kind = ClientAccountEntryKind.VisitPayment,
            Amount = amount,
            Currency = "GBP",
            Method = PaymentMethod.Cash,
            At = at,
        });
        await db.SaveChangesAsync();
    }

    private Task<SalonReport> ReportAsync(Guid tenantId, ReportPeriod period)
        => ScopedAsync<IReportService, SalonReport>(tenantId, s => s.GetAsync(null, period.From, period.To));

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