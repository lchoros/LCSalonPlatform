using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Reports;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// Giving the money back for a counter sale (ADR 0054): at the price it was sold at, as a line of money that points
/// at the sale and has no client either, the units back on the shelf only when they can be sold again. Nobody gets
/// more back than they kept, even from two tills at once. The till and the reports count it like any other return.
/// A shampoo sells at £12.50 and costs £6.
/// </summary>
[Collection(SharedDatabase.Name)]
public class CounterSaleReturnTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/payments/sales";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly LocalDate Today = SystemClock.Instance.GetCurrentInstant().InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _shampoo;
    private Guid _oil;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _shampoo = await ProductAsync("Shampoo 250 ml", 12.50m, cost: 6m, onHand: 8);
        _oil = await ProductAsync("Hair oil 50 ml", 18m, cost: null, onHand: 3);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_money_goes_back_at_the_price_sold_as_a_line_that_points_at_the_sale_and_the_units_go_back_on_the_shelf()
    {
        var sale = await SellAsync([(_shampoo, 3), (_oil, 1)]);
        await SetPriceAsync(_shampoo, 20m);
        var line = await LineAsync(sale, _shampoo);

        var back = await SalesAsync(s => s.ReturnAsync(sale, line, 2, PaymentMethod.Cash, backOnShelf: true, null));

        back.Amount.ShouldBe(25m, "the price it was sold at, not today's");
        await InScopeAsync(async db =>
        {
            var entry = await db.ClientAccountEntries.AsNoTracking().SingleAsync(e => e.Kind == ClientAccountEntryKind.ProductReturn);
            (entry.ClientId, entry.AppointmentId, entry.RelatedEntryId, entry.Amount, entry.Method).ShouldBe((null, null, sale, 25m, PaymentMethod.Cash));
            (await db.CounterSaleLines.AsNoTracking().SingleAsync(l => l.Id == line)).ReturnedQuantity.ShouldBe(2);
            (await db.Products.AsNoTracking().SingleAsync(p => p.Id == _shampoo)).StockOnHand.ShouldBe(7, "eight, three sold, two back");
        });

        var day = await SalesAsync(s => s.ListAsync(null));
        var record = day.Sales.ShouldHaveSingleItem();
        (record.Total.Amount, record.Returned.Amount).ShouldBe((55.50m, 25m));
        record.Lines.Select(l => (l.Name, l.Quantity, l.ReturnedQuantity, l.Kept)).ShouldBe([("Hair oil 50 ml", 1, 0, 1), ("Shampoo 250 ml", 3, 2, 1)]);
    }

    [Fact]
    public async Task An_opened_bottle_gets_its_money_back_and_stays_off_the_shelf()
    {
        var sale = await SellAsync([(_shampoo, 1)]);

        var line = await LineAsync(sale, _shampoo);

        await SalesAsync(s => s.ReturnAsync(sale, line, 1, PaymentMethod.Cash, backOnShelf: false, null));

        await InScopeAsync(async db => (await db.Products.AsNoTracking().SingleAsync(p => p.Id == _shampoo)).StockOnHand.ShouldBe(7));
    }

    [Fact]
    public async Task Nobody_gets_more_back_than_they_kept()
    {
        var sale = await SellAsync([(_shampoo, 2)]);
        var line = await LineAsync(sale, _shampoo);

        (await RefusalAsync(s => s.ReturnAsync(sale, line, 3, PaymentMethod.Cash, true, null))).ShouldBe(CounterSaleRefusal.ReturnMoreThanKept);
        (await RefusalAsync(s => s.ReturnAsync(sale, line, 0, PaymentMethod.Cash, true, null))).ShouldBe(CounterSaleRefusal.ReturnMoreThanKept);
        await SalesAsync(s => s.ReturnAsync(sale, line, 2, PaymentMethod.Cash, true, null));
        (await RefusalAsync(s => s.ReturnAsync(sale, line, 1, PaymentMethod.Cash, true, null))).ShouldBe(CounterSaleRefusal.ReturnMoreThanKept, "both came back already");

        await InScopeAsync(async db =>
        {
            (await db.ClientAccountEntries.AsNoTracking().CountAsync(e => e.Kind == ClientAccountEntryKind.ProductReturn)).ShouldBe(1);
            (await db.Products.AsNoTracking().SingleAsync(p => p.Id == _shampoo)).StockOnHand.ShouldBe(8);
        });
    }

    [Fact]
    public async Task Two_tills_cannot_both_give_the_money_back_for_the_last_unit()
    {
        var sale = await SellAsync([(_shampoo, 1)]);
        var line = await LineAsync(sale, _shampoo);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try
            {
                await SalesAsync(s => s.ReturnAsync(sale, line, 1, PaymentMethod.Cash, false, null));
                return true;
            }
            catch (CounterSaleException ex) when (ex.Refusal == CounterSaleRefusal.ReturnMoreThanKept)
            {
                return false;
            }
        }));

        attempts.Count(done => done).ShouldBe(1);
        await InScopeAsync(async db =>
        {
            (await db.ClientAccountEntries.AsNoTracking().CountAsync(e => e.Kind == ClientAccountEntryKind.ProductReturn)).ShouldBe(1);
            (await db.CounterSaleLines.AsNoTracking().SingleAsync(l => l.Id == line)).ReturnedQuantity.ShouldBe(1);
        });
    }

    [Fact]
    public async Task The_till_and_the_reports_count_it_like_any_other_return_and_the_profit_is_of_what_was_kept()
    {
        var sale = await SellAsync([(_shampoo, 2)]);
        var line = await LineAsync(sale, _shampoo);
        await SalesAsync(s => s.ReturnAsync(sale, line, 1, PaymentMethod.Cash, true, null));

        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.Single(t => t.Method == PaymentMethod.Cash).Amount.Amount.ShouldBe(12.50m);
        var report = await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(ReportPreset.ThisMonth, null, null));
        var retail = report.Retail;
        (retail.Sold.Amount, retail.Returned.Amount, retail.Net.Amount, retail.Profit?.Amount).ShouldBe((25m, 12.50m, 12.50m, 6.50m));
        report.Takings.Total.Amount.ShouldBe(12.50m);

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var html = await GetAsync(owner, "/admin/payments/cash-up");
        html.ShouldContain("data-kind=\"ProductReturn\"");
    }

    [Fact]
    public async Task The_desk_finds_the_sale_by_its_day_and_records_the_return_on_the_page()
    {
        var sale = await SellAsync([(_shampoo, 2)]);
        var line = await LineAsync(sale, _shampoo);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var page = await GetAsync(desk, Path);
        page.ShouldContain("Sales at the counter");
        page.ShouldContain($"data-sale=\"{sale}\"");
        page.ShouldContain("2 × £12.50");
        page.ShouldContain($"action=\"{Path}/{sale}/lines/{line}/return\"");
        (await GetAsync(desk, "/admin/payments/sale")).ShouldContain($"href=\"{Path}\"");

        var response = await desk.PostAsync(new Uri($"{Path}/{sale}/lines/{line}/return", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(page)),
            new("quantity", "1"),
            new("method", "Cash"),
            new("backOnShelf", "true"),
            new("date", LocalDatePattern.Iso.Format(Today)),
        ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe($"{Path}?date={LocalDatePattern.Iso.Format(Today)}");
        var after = await GetAsync(desk, Path);
        after.ShouldContain("The return has been recorded: £12.50 given back.");
        after.ShouldContain("data-testid=\"sale-returned\">Given back: £12.50<");
        after.ShouldContain("data-testid=\"line-returned\">Returned: 1<");

        // Too many: the page says why and nothing more is recorded.
        var tooMany = await desk.PostAsync(new Uri($"{Path}/{sale}/lines/{line}/return", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(after)),
            new("quantity", "5"),
            new("method", "Cash"),
        ]));
        tooMany.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(desk, Path)).ShouldContain("The buyer does not have that many of this product left.");

        var yesterday = await GetAsync(desk, $"{Path}?date={LocalDatePattern.Iso.Format(Today.PlusDays(-1))}");
        yesterday.ShouldContain("data-testid=\"no-sales\"");
        yesterday.ShouldContain("data-testid=\"next-day\"");
    }

    [Fact]
    public async Task Another_salons_sale_is_a_missing_one_and_staff_do_not_get_in()
    {
        var sale = await SellAsync([(_shampoo, 1)]);
        var line = await LineAsync(sale, _shampoo);
        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var theirs = await GetAsync(polish, Path);
        theirs.ShouldContain("data-testid=\"no-sales\"");
        var stolen = await polish.PostAsync(new Uri($"{Path}/{sale}/lines/{line}/return", UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(theirs)),
            new("quantity", "1"),
            new("method", "Cash"),
        ]));
        stolen.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var denied = await staff.GetAsync(new Uri(Path, UriKind.Relative));
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var noToken = await desk.PostAsync(new Uri($"{Path}/{sale}/lines/{line}/return", UriKind.Relative), new FormUrlEncodedContent([new("quantity", "1"), new("method", "Cash")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await RefusalAsync(s => s.ReturnAsync(sale, Guid.NewGuid(), 1, PaymentMethod.Cash, true, null))).ShouldBe(CounterSaleRefusal.SaleNotFound);

        await InScopeAsync(async db => (await db.ClientAccountEntries.AsNoTracking().AnyAsync(e => e.Kind == ClientAccountEntryKind.ProductReturn)).ShouldBeFalse());
    }

    private async Task<Guid> SellAsync(IReadOnlyList<(Guid, int)> lines)
        => (await SalesAsync(s => s.SellAsync(lines, PaymentMethod.Cash, null))).EntryId;

    private async Task<Guid> LineAsync(Guid sale, Guid product)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().CounterSaleLines.AsNoTracking()
            .Where(l => l.PaymentEntryId == sale && l.ProductId == product).Select(l => l.Id).SingleAsync();
    }

    private async Task<CounterSaleRefusal> RefusalAsync<T>(Func<ICounterSaleService, Task<T>> act)
        => (await Should.ThrowAsync<CounterSaleException>(() => SalesAsync(act))).Refusal;

    private Task<T> SalesAsync<T>(Func<ICounterSaleService, Task<T>> act) => ScopedAsync(act);

    private async Task<T> ScopedAsync<TService, T>(Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync(Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
    }

    private async Task<Guid> ProductAsync(string name, decimal price, decimal? cost, int onHand)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, CostPrice = cost, Currency = "GBP", StockOnHand = onHand };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private async Task SetPriceAsync(Guid productId, decimal price)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products
            .Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.RetailPrice, price));
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}