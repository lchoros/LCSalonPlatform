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
/// Profit on products in the reports (ADR 0052): the cost of the day is kept on the bill line when it is paid, so the
/// profit of a past sale does not move when the cost changes. Units brought back earn nothing, units sold without a
/// cost are counted apart, and another salon sees none of it. A shampoo sells at £12.50 and costs £6.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ProductProfitTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private Guid _shampoo;
    private Guid _oil;
    private int _visits;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _shampoo = await ProductAsync("Shampoo 250 ml", 12.50m, cost: 6m);
        _oil = await ProductAsync("Hair oil 50 ml", 18m, cost: null);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_cost_of_the_day_is_kept_on_the_line_so_a_later_change_does_not_move_the_profit()
    {
        var visit = await VisitAsync();
        await AccountAsync(s => s.AddProductAsync(visit, _shampoo, 3, null));
        (await LinesAsync(visit)).ShouldHaveSingleItem().UnitCost.ShouldBeNull("nothing is sold until the bill is paid");
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));

        (await LinesAsync(visit)).ShouldHaveSingleItem().UnitCost.ShouldBe(6m);
        var before = (await ReportAsync(_studio.Id)).Retail;
        (before.Profit?.Amount, before.ProfitShare, before.UnitsWithoutCost).ShouldBe((19.50m, 52m, 0));

        // The supplier puts the price up. What was sold keeps the cost it was sold at; the next sale has the new one.
        await SetCostAsync(_shampoo, 10m);
        (await ReportAsync(_studio.Id)).Retail.Profit?.Amount.ShouldBe(19.50m);
        var later = await VisitAsync();
        await AccountAsync(s => s.AddProductAsync(later, _shampoo, 1, null));
        await AccountAsync(s => s.PayBillAsync(later, PaymentMethod.Cash, [], null));

        var after = (await ReportAsync(_studio.Id)).Retail;
        (after.Profit?.Amount, after.ProfitShare).ShouldBe((22m, 44m), "19.50 and 2.50, of the 50.00 they were sold for");
        after.TopProducts.ShouldHaveSingleItem().Profit?.Amount.ShouldBe(22m);
    }

    [Fact]
    public async Task Units_sold_without_a_cost_are_counted_apart_and_units_brought_back_earn_nothing()
    {
        var visit = await VisitAsync();
        await AccountAsync(s => s.AddProductAsync(visit, _shampoo, 3, null));
        await AccountAsync(s => s.AddProductAsync(visit, _oil, 2, null));
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        var shampooLine = (await LinesAsync(visit)).Single(l => l.ProductId == _shampoo).Id;
        await AccountAsync(s => s.ReturnProductAsync(visit, shampooLine, 1, PaymentMethod.Cash, true, null));

        var retail = (await ReportAsync(_studio.Id)).Retail;

        (retail.Profit?.Amount, retail.ProfitShare, retail.UnitsWithoutCost).ShouldBe((13m, 52m, 2));
        retail.TopProducts.Select(p => (p.Name, p.Profit?.Amount)).ShouldBe([("Shampoo 250 ml", 13m), ("Hair oil 50 ml", null)], ignoreOrder: true);

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var html = await GetAsync(owner, "/admin/reports");
        html.ShouldContain("Profit on products");
        html.ShouldContain("data-testid=\"retail-profit\">£13.00 (52%)<");
        html.ShouldContain("data-testid=\"retail-without-cost\">2 units were sold with no cost on record and are left out of the profit.<");
        html.ShouldContain("data-testid=\"top-product-profit\">Profit: £13.00<");
    }

    [Fact]
    public async Task With_no_cost_on_record_the_report_states_no_profit_and_says_why()
    {
        var visit = await VisitAsync();
        await AccountAsync(s => s.AddProductAsync(visit, _oil, 1, null));
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));

        var retail = (await ReportAsync(_studio.Id)).Retail;
        retail.Profit.ShouldBeNull("not zero: nobody knows");

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var html = await GetAsync(owner, "/admin/reports");
        html.ShouldNotContain("data-testid=\"retail-profit\"");
        html.ShouldContain("1 unit was sold with no cost on record and is left out of the profit.");
    }

    [Fact]
    public async Task A_product_sold_under_its_cost_is_a_loss_with_its_minus()
    {
        await SetCostAsync(_shampoo, 14m);
        var visit = await VisitAsync();
        await AccountAsync(s => s.AddProductAsync(visit, _shampoo, 2, null));
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));

        var retail = (await ReportAsync(_studio.Id)).Retail;
        (retail.Profit?.Amount, retail.ProfitShare).ShouldBe((-3m, -12m));

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        // Both figures carry a real minus sign (U+2212), as money does everywhere else.
        (await GetAsync(owner, "/admin/reports")).ShouldContain("data-testid=\"retail-profit\">−£3.00 (−12%)<");
    }

    [Fact]
    public async Task Another_salon_sees_none_of_it()
    {
        var visit = await VisitAsync();
        await AccountAsync(s => s.AddProductAsync(visit, _shampoo, 3, null));
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));

        var retail = (await ReportAsync(_polish.Id)).Retail;

        retail.HasAny.ShouldBeFalse();
        retail.Profit.ShouldBeNull();
        retail.UnitsWithoutCost.ShouldBe(0);
    }

    /// <summary>An arrived visit earlier today; each one a little earlier than the last so no two overlap.</summary>
    private Task<Guid> VisitAsync()
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now - Duration.FromMinutes(40 * ++_visits), AppointmentStatus.Arrived);

    private async Task<Guid> ProductAsync(string name, decimal price, decimal? cost)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, CostPrice = cost, Currency = "GBP" };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, 20, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        return product.Id;
    }

    private async Task SetCostAsync(Guid productId, decimal cost)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.CostPrice, cost));
    }

    private async Task<List<VisitProductLine>> LinesAsync(Guid visit)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().VisitProductLines.AsNoTracking().Where(l => l.AppointmentId == visit).ToListAsync();
    }

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