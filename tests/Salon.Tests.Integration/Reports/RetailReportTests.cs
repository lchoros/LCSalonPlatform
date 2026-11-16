using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Services.Reports;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Reports;

/// <summary>
/// Products in the reports (ADR 0044): what was sold over the counter in a period, by product, counted on the day the
/// money was recorded like every other number of the takings, so the section and the takings never disagree. The
/// shampoo costs £12.50, the conditioner £9, a cut £20.
/// </summary>
[Collection(SharedDatabase.Name)]
public class RetailReportTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string ReportsPath = "/admin/reports";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private Guid _shampoo;
    private Guid _conditioner;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _shampoo = await ProductAsync("Shampoo 250 ml", 12.50m, onHand: 8);
        _conditioner = await ProductAsync("Conditioner 250 ml", 9m, onHand: 8);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Products_are_added_up_by_product_and_the_lines_add_up_to_the_money()
    {
        await SellTwoBillsAsync();

        var report = await ReportAsync(_studio.Id);

        var retail = report.Retail;
        retail.HasAny.ShouldBeTrue();
        retail.Sold.Amount.ShouldBe(59m, "three shampoos and a conditioner on one bill, one shampoo on another");
        retail.Returned.Amount.ShouldBe(0m);
        retail.Net.Amount.ShouldBe(59m);
        retail.Units.ShouldBe(5);
        retail.Bills.ShouldBe(2);
        retail.ProductCount.ShouldBe(2);
        retail.TopProducts.Select(p => (p.Name, p.Units, p.Value.Amount)).ShouldBe([("Shampoo 250 ml", 4, 50m), ("Conditioner 250 ml", 1, 9m)]);
        retail.TopProducts.Sum(p => p.Value.Amount).ShouldBe(retail.Sold.Amount, "the products are the money, product by product");
        report.Takings.ByKind.Single(k => k.Kind == ClientAccountEntryKind.ProductSale).Amount.Amount.ShouldBe(retail.Sold.Amount);
        report.Takings.Total.Amount.ShouldBe(99m, "two cuts and the products");
        retail.ShareOfTakings.ShouldBe(59.6m);
    }

    [Fact]
    public async Task A_return_takes_its_money_off_the_products_of_the_day_it_was_given_back()
    {
        var (visit, line) = await SellTwoBillsAsync();
        await AccountAsync(s => s.ReturnProductAsync(visit, line, 1, PaymentMethod.Cash, true, null));

        var report = await ReportAsync(_studio.Id);

        var retail = report.Retail;
        retail.Sold.Amount.ShouldBe(59m, "what was sold stays sold");
        retail.Returned.Amount.ShouldBe(12.50m);
        retail.Net.Amount.ShouldBe(46.50m);
        retail.Units.ShouldBe(5);
        report.Takings.Total.Amount.ShouldBe(86.50m);
        retail.ShareOfTakings.ShouldBe(53.8m);
    }

    [Fact]
    public async Task A_product_that_is_on_a_bill_but_not_paid_for_is_not_sold_yet()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30));
        await AccountAsync(s => s.AddProductAsync(visit, _shampoo, 2, null));

        var report = await ReportAsync(_studio.Id);

        report.Retail.HasAny.ShouldBeFalse();
        report.Retail.Units.ShouldBe(0);
        report.Retail.TopProducts.ShouldBeEmpty();
        report.Retail.ShareOfTakings.ShouldBeNull();
    }

    [Fact]
    public async Task Sales_count_in_the_period_the_money_was_recorded_and_in_no_other()
    {
        await SellTwoBillsAsync();
        var today = Now.InZone(DateTimeZoneProviders.Tzdb["Europe/London"]).Date;

        var before = await ScopedAsync<IReportService, SalonReport>(_studio.Id, s => s.GetAsync(null, today.PlusDays(-14), today.PlusDays(-8)));
        var around = await ScopedAsync<IReportService, SalonReport>(_studio.Id, s => s.GetAsync(null, today.PlusDays(-1), today.PlusDays(1)));

        before.Retail.HasAny.ShouldBeFalse();
        before.Retail.TopProducts.ShouldBeEmpty();
        around.Retail.Sold.Amount.ShouldBe(59m);
    }

    [Fact]
    public async Task A_renamed_product_is_one_line_under_the_name_it_has_now()
    {
        await SellTwoBillsAsync();
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products
                .Where(p => p.Id == _shampoo)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Shampoo 300 ml"));
        }

        var second = await VisitAsync(Now - Duration.FromHours(3));
        await AccountAsync(s => s.AddProductAsync(second, _shampoo, 1, null));
        await AccountAsync(s => s.PayBillAsync(second, PaymentMethod.Cash, [], null));

        var top = (await ReportAsync(_studio.Id)).Retail.TopProducts;

        top.Select(p => (p.Name, p.Units)).ShouldBe([("Shampoo 300 ml", 5), ("Conditioner 250 ml", 1)]);
    }

    [Fact]
    public async Task Another_salon_sees_none_of_it()
    {
        await SellTwoBillsAsync();

        var other = await ReportAsync(_polish.Id);

        other.Retail.HasAny.ShouldBeFalse();
        other.Retail.TopProducts.ShouldBeEmpty();
        other.Retail.Sold.Currency.ShouldBe("PLN");
    }

    [Fact]
    public async Task The_page_shows_the_products_to_an_owner_and_hides_the_section_while_nothing_was_sold()
    {
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await GetAsync(owner, ReportsPath)).ShouldNotContain("data-testid=\"retail\"");

        var (visit, line) = await SellTwoBillsAsync();
        await AccountAsync(s => s.ReturnProductAsync(visit, line, 1, PaymentMethod.Cash, true, null));

        var html = await GetAsync(owner, ReportsPath);

        html.ShouldContain("data-testid=\"retail\"");
        html.ShouldContain("Products sold");
        html.ShouldContain("data-testid=\"retail-sold\">£59.00<");
        html.ShouldContain("data-testid=\"retail-returned\">−£12.50<");
        html.ShouldContain("data-testid=\"retail-net\">£46.50<");
        html.ShouldContain("data-testid=\"retail-units\">5<");
        html.ShouldContain("data-testid=\"retail-bills\">2<");
        html.ShouldContain("data-testid=\"retail-share\">53.8%<");
        html.ShouldContain("Shampoo 250 ml");
        html.ShouldContain("4 sold");
        html.ShouldContain("Conditioner 250 ml");
    }

    /// <summary>
    /// Two arrived visits paid today: three shampoos and a conditioner by card on the first, one shampoo in cash on the
    /// second. Returns the first visit and its shampoo line.
    /// </summary>
    private async Task<(Guid Visit, Guid ShampooLine)> SellTwoBillsAsync()
    {
        var first = await VisitAsync(Now - Duration.FromMinutes(60));
        await AccountAsync(s => s.AddProductAsync(first, _shampoo, 3, null));
        await AccountAsync(s => s.AddProductAsync(first, _conditioner, 1, null));
        await AccountAsync(s => s.PayBillAsync(first, PaymentMethod.CardAtSalonTerminal, [], null));
        var second = await VisitAsync(Now - Duration.FromMinutes(30));
        await AccountAsync(s => s.AddProductAsync(second, _shampoo, 1, null));
        await AccountAsync(s => s.PayBillAsync(second, PaymentMethod.Cash, [], null));

        await using var scope = factory.CreateTenantScope(_studio.Id);
        var line = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().VisitProductLines.AsNoTracking()
            .SingleAsync(l => l.AppointmentId == first && l.ProductId == _shampoo);
        return (first, line.Id);
    }

    private Task<Guid> VisitAsync(Instant start)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, AppointmentStatus.Arrived);

    private async Task<Guid> ProductAsync(string name, decimal price, int onHand)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, Currency = "GBP" };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, onHand, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        return product.Id;
    }

    /// <summary>This month, which is where today's money is.</summary>
    private Task<SalonReport> ReportAsync(Guid tenantId)
        => ScopedAsync<IReportService, SalonReport>(tenantId, s => s.GetAsync(ReportPreset.ThisMonth, null, null));

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