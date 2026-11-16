using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Reports;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// The takings of each day as bars (ADR 0048): one bar per day of the period, with or without money, the best day
/// reaching the top, a day with more out than in hanging red under the line. Plain SVG shapes whose numbers keep a
/// point as the decimal sign in every language. No chart for one day, for more than 62 days or without money. The
/// week here is Monday 2 to Sunday 8 March 2026.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ReportChartTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string Week = "/admin/reports?from=2026-03-02&to=2026-03-08";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Monday = new(2026, 3, 2);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Every_day_of_the_week_is_a_bar_and_the_best_day_reaches_the_top()
    {
        await MoneyAsync(_studio.Id, _clientId, Monday.PlusDays(1), 50m);
        await MoneyAsync(_studio.Id, _clientId, Monday.PlusDays(5), 150m);
        await MoneyAsync(_studio.Id, _clientId, Monday.PlusDays(5), 50m);

        var daily = (await ReportAsync(_studio.Id, Monday, Monday.PlusDays(6))).Takings.Daily;
        daily.Select(d => d.Day).ShouldBe(Enumerable.Range(0, 7).Select(Monday.PlusDays));
        daily.Select(d => d.Amount.Amount).ShouldBe([0m, 50m, 0m, 0m, 0m, 200m, 0m]);

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var html = await GetAsync(owner, Week);

        var bars = Bars(html);
        bars.Select(b => b.Day).ShouldBe(["2026-03-02", "2026-03-03", "2026-03-04", "2026-03-05", "2026-03-06", "2026-03-07", "2026-03-08"]);
        var saturday = bars.Single(b => b.Day == "2026-03-07");
        (saturday.Y, saturday.Height).ShouldBe(("0", "160"), "the best day is the whole height");
        var tuesday = bars.Single(b => b.Day == "2026-03-03");
        (tuesday.Y, tuesday.Height).ShouldBe(("120", "40"), "a quarter of the best day");
        bars.Single(b => b.Day == "2026-03-02").Height.ShouldBe("0", "a day without money keeps its place and has no bar");
        bars.ShouldAllBe(b => b.Class == "fill-stone-800");
        bars.Select(b => b.Width).Distinct().ShouldHaveSingleItem().ShouldBe("66.43");
        html.ShouldContain("viewBox=\"0 0 620 160\"");
        html.ShouldContain("Takings each day");
        html.ShouldContain("<title>Tuesday 3 March 2026: £50.00</title>");
        html.ShouldContain("aria-label=\"Best day: Saturday 7 March 2026, £200.00.\"");
        html.ShouldContain("data-testid=\"chart-best\">Best day: Saturday 7 March 2026, £200.00.<");
        html.ShouldContain("data-testid=\"chart-first\">2 Mar<");
        html.ShouldContain("data-testid=\"chart-last\">8 Mar<");
    }

    [Fact]
    public async Task A_day_with_more_money_out_than_in_hangs_red_under_the_line()
    {
        await MoneyAsync(_studio.Id, _clientId, Monday, 100m, ClientAccountEntryKind.DepositPaid);
        await MoneyAsync(_studio.Id, _clientId, Monday.PlusDays(2), 100m, ClientAccountEntryKind.DepositReturned);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var html = await GetAsync(owner, Week);

        var bars = Bars(html);
        var monday = bars.Single(b => b.Day == "2026-03-02");
        (monday.Y, monday.Height, monday.Class).ShouldBe(("0", "80", "fill-stone-800"));
        var wednesday = bars.Single(b => b.Day == "2026-03-04");
        (wednesday.Y, wednesday.Height, wednesday.Class).ShouldBe(("80", "80", "fill-red-700"));
        html.ShouldContain("<line x1=\"0\" x2=\"620\" y1=\"80\" y2=\"80\"");
        html.ShouldContain("Best day: Monday 2 March 2026, £100.00.");
    }

    [Fact]
    public async Task One_day_more_than_two_months_or_no_money_have_no_chart()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await GetAsync(owner, Week)).ShouldNotContain("data-testid=\"takings-chart\"", Case.Sensitive, "nothing to draw while no money was recorded");

        await MoneyAsync(_studio.Id, _clientId, Monday, 100m);

        (await GetAsync(owner, "/admin/reports?from=2026-03-02&to=2026-03-02")).ShouldNotContain("data-testid=\"takings-chart\"", Case.Sensitive, "one day is one number");
        (await GetAsync(owner, "/admin/reports?from=2026-01-01&to=2026-03-04")).ShouldNotContain("data-testid=\"takings-chart\"", Case.Sensitive, "63 days are too many bars");
        Bars(await GetAsync(owner, "/admin/reports?from=2026-01-02&to=2026-03-04")).Count.ShouldBe(62);
        Bars(await GetAsync(owner, "/admin/reports?from=2026-03-02&to=2026-03-03")).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_Polish_salon_reads_its_own_days_in_Polish_and_the_shapes_keep_a_decimal_point()
    {
        await MoneyAsync(_studio.Id, _clientId, Monday, 999m);
        var anna = await VisitSeed.ClientAsync(factory, _polish.Id, "Anna", "Nowak", "601 234 567");
        await MoneyAsync(_polish.Id, anna, Monday.PlusDays(1), 45.5m, currency: "PLN");
        await MoneyAsync(_polish.Id, anna, Monday.PlusDays(5), 91m, currency: "PLN");
        using var owner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var html = await GetAsync(owner, Week);

        var bars = Bars(html);
        bars.Count.ShouldBe(7);
        bars.ShouldAllBe(b => Plain().IsMatch(b.X) && Plain().IsMatch(b.Y) && Plain().IsMatch(b.Width) && Plain().IsMatch(b.Height));
        bars.Single(b => b.Day == "2026-03-02").Height.ShouldBe("0", "the other salon's Monday is not this salon's");
        bars.Single(b => b.Day == "2026-03-03").Height.ShouldBe("80");
        html.ShouldContain("Najlepszy dzień: sobota 7 marca 2026, 91,00");
        html.ShouldContain("Utarg w kolejnych dniach");
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static List<(string X, string Y, string Width, string Height, string Class, string Day)> Bars(string html)
        => Bar().Matches(html)
            .Select(m => (m.Groups["x"].Value, m.Groups["y"].Value, m.Groups["w"].Value, m.Groups["h"].Value, m.Groups["class"].Value, m.Groups["day"].Value))
            .ToList();

    /// <summary>Money recorded at noon of a past day, written straight to the ledger: the services only ever record "now".</summary>
    private async Task MoneyAsync(Guid tenantId, Guid clientId, LocalDate day, decimal amount, ClientAccountEntryKind kind = ClientAccountEntryKind.VisitPayment, string currency = "GBP")
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.ClientAccountEntries.Add(new ClientAccountEntry
        {
            ClientId = clientId,
            Kind = kind,
            Amount = amount,
            Currency = currency,
            Method = PaymentMethod.Cash,
            At = London.AtStrictly(day + new LocalTime(12, 0)).ToInstant(),
        });
        await db.SaveChangesAsync();
    }

    private async Task<SalonReport> ReportAsync(Guid tenantId, LocalDate from, LocalDate to)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IReportService>().GetAsync(null, from, to);
    }

    [GeneratedRegex("<rect x=\"(?<x>[^\"]*)\" y=\"(?<y>[^\"]*)\" width=\"(?<w>[^\"]*)\" height=\"(?<h>[^\"]*)\" class=\"(?<class>[^\"]*)\" data-testid=\"chart-bar\" data-day=\"(?<day>[^\"]*)\">")]
    private static partial Regex Bar();

    [GeneratedRegex(@"^\d+(\.\d+)?$")]
    private static partial Regex Plain();
}