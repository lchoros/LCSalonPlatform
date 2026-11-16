using System.Net;
using System.Text;
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
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// A sale at the counter without a visit (ADR 0053): one line of money with no client and no visit, a line per
/// product with its price and cost of the day, every unit off its shelf. The till, the takings, the reports and the
/// money file count it like any other product sale. Only that one kind of line may lack a client: the database
/// refuses any other. A shampoo sells at £12.50 and costs £6; a hair oil sells at £18.
/// </summary>
[Collection(SharedDatabase.Name)]
public class CounterSaleTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Path = "/admin/payments/sale";
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly string Today = LocalDatePattern.Iso.Format(SystemClock.Instance.GetCurrentInstant().InZone(London).Date);

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _shampoo;
    private Guid _oil;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        _shampoo = await ProductAsync(_studio.Id, "Shampoo 250 ml", 12.50m, cost: 6m, onHand: 8);
        _oil = await ProductAsync(_studio.Id, "Hair oil 50 ml", 18m, cost: null, onHand: 3);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_sale_is_one_line_of_money_without_a_client_with_its_products_and_the_units_leave_the_shelf()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "desk@studio-20.test", Role.Receptionist);

        var receipt = await SalesAsync(_studio.Id, s => s.SellAsync([(_shampoo, 2), (_oil, 1)], PaymentMethod.CardAtSalonTerminal, user.Id));

        (receipt.Total.Amount, receipt.Total.Currency, receipt.Units, receipt.Method).ShouldBe((43m, "GBP", 3, PaymentMethod.CardAtSalonTerminal));
        await InScopeAsync(_studio.Id, async db =>
        {
            var entry = await db.ClientAccountEntries.AsNoTracking().SingleAsync();
            entry.Id.ShouldBe(receipt.EntryId);
            (entry.Kind, entry.ClientId, entry.AppointmentId, entry.Amount, entry.Method, entry.ByUserId)
                .ShouldBe((ClientAccountEntryKind.ProductSale, null, null, 43m, PaymentMethod.CardAtSalonTerminal, user.Id));
            entry.ProviderReference.ShouldNotBeNullOrEmpty();
            var lines = await db.CounterSaleLines.AsNoTracking().OrderBy(l => l.ProductName).ToListAsync();
            lines.Select(l => (l.PaymentEntryId, l.ProductName, l.Quantity, l.UnitPrice, l.UnitCost)).ShouldBe(
            [
                (entry.Id, "Hair oil 50 ml", 1, 18m, null),
                (entry.Id, "Shampoo 250 ml", 2, 12.50m, 6m),
            ]);
            var shelves = await db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.StockOnHand);
            (shelves[_shampoo], shelves[_oil]).ShouldBe((6, 2));
            (await db.StockMovements.AsNoTracking().CountAsync(m => m.Kind == StockMovementKind.Sold)).ShouldBe(2, "each product has its line in the shelf's history");
        });
    }

    [Fact]
    public async Task The_till_the_report_and_the_money_file_count_it_like_any_other_product_sale()
    {
        await SalesAsync(_studio.Id, s => s.SellAsync([(_shampoo, 2)], PaymentMethod.Cash, null));
        await SalesAsync(_studio.Id, s => s.SellAsync([(_shampoo, 1), (_oil, 1)], PaymentMethod.Cash, null));

        var day = await ScopedAsync<ICashUpService, CashDay>(_studio.Id, s => s.GetDayAsync(null));
        day.Totals.Single(t => t.Method == PaymentMethod.Cash).Amount.Amount.ShouldBe(55.50m);
        day.Movements.Count.ShouldBe(2);
        day.Movements.ShouldAllBe(m => m.ClientName.Length == 0 && m.AppointmentId == null && m.Kind == ClientAccountEntryKind.ProductSale);

        var report = await ScopedAsync<IReportService, SalonReport>(_studio.Id, s => s.GetAsync(ReportPreset.ThisMonth, null, null));
        report.Takings.Total.Amount.ShouldBe(55.50m);
        var retail = report.Retail;
        (retail.Sold.Amount, retail.Units, retail.Bills, retail.CounterSales).ShouldBe((55.50m, 4, 0, 2));
        retail.TopProducts.Select(p => (p.Name, p.Units, p.Value.Amount)).ShouldBe([("Shampoo 250 ml", 3, 37.50m), ("Hair oil 50 ml", 1, 18m)]);
        (retail.Profit?.Amount, retail.UnitsWithoutCost).ShouldBe((19.50m, 1), "three shampoos at 6.50 each; the oil has no cost");

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var till = await GetAsync(owner, "/admin/payments/cash-up");
        till.ShouldContain("· Counter sale<");
        till.ShouldContain("href=\"/admin/payments/sale\" class=\"btn btn-compact btn-secondary\" data-testid=\"counter-sale\"");
        (await GetAsync(owner, "/admin/reports")).ShouldContain("data-testid=\"retail-counter-sales\">2<");
        var file = await owner.GetAsync(new Uri($"/admin/reports/money.csv?from={Today}&to={Today}", UriKind.Relative));
        var bytes = await file.Content.ReadAsByteArrayAsync();
        var rows = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        rows.Length.ShouldBe(2);
        rows[0].ShouldContain(",,Products sold,,Cash,25.00,GBP,", Case.Sensitive, "no client and no service: the cells are empty");
    }

    [Fact]
    public async Task What_cannot_be_sold_is_refused_with_its_reason_and_nothing_is_recorded()
    {
        var retired = await ProductAsync(_studio.Id, "Old wax", 9m, cost: null, onHand: 1, active: false);
        var forTheChair = await ProductAsync(_studio.Id, "Colour tube", null, cost: 4m, onHand: 5);
        var theirs = await ProductAsync(_polish.Id, "Szampon", 45.90m, cost: null, onHand: 5, currency: "PLN");
        var eleven = new List<(Guid, int)>();
        for (var i = 0; i < 11; i++)
        {
            eleven.Add((await ProductAsync(_studio.Id, $"Product {i}", 1m, cost: null, onHand: 1), 1));
        }

        (await RefusalAsync([])).ShouldBe(CounterSaleRefusal.NothingChosen);
        (await RefusalAsync([(retired, 1)])).ShouldBe(CounterSaleRefusal.ProductNotForSale);
        (await RefusalAsync([(forTheChair, 1)])).ShouldBe(CounterSaleRefusal.ProductNotForSale);
        (await RefusalAsync([(theirs, 1)])).ShouldBe(CounterSaleRefusal.ProductNotForSale, "another salon's product looks like a missing one");
        (await RefusalAsync([(Guid.NewGuid(), 1)])).ShouldBe(CounterSaleRefusal.ProductNotForSale);
        (await RefusalAsync([(_shampoo, 0)])).ShouldBe(CounterSaleRefusal.QuantityOutOfRange);
        (await RefusalAsync([(_shampoo, 100)])).ShouldBe(CounterSaleRefusal.QuantityOutOfRange);
        (await RefusalAsync(eleven)).ShouldBe(CounterSaleRefusal.TooManyLines);
        (await RefusalAsync([(_shampoo, 1), (_shampoo, 1)])).ShouldBe(CounterSaleRefusal.TooManyLines, "the form adds a repeated product up before it gets here");

        await InScopeAsync(_studio.Id, async db =>
        {
            (await db.ClientAccountEntries.AsNoTracking().AnyAsync()).ShouldBeFalse();
            (await db.CounterSaleLines.AsNoTracking().AnyAsync()).ShouldBeFalse();
            (await db.Products.AsNoTracking().SingleAsync(p => p.Id == _shampoo)).StockOnHand.ShouldBe(8);
        });
    }

    [Fact]
    public async Task Only_a_counter_sale_may_be_a_line_without_a_client_and_the_database_says_so()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.ClientAccountEntries.Add(new ClientAccountEntry
        {
            ClientId = null,
            Kind = ClientAccountEntryKind.VisitPayment,
            Amount = 20m,
            Currency = "GBP",
            Method = PaymentMethod.Cash,
            At = SystemClock.Instance.GetCurrentInstant(),
        });

        var refused = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        refused.InnerException.ShouldNotBeNull().Message.ShouldContain("ck_client_account_entries_client_unless_counter_sale");
    }

    [Fact]
    public async Task The_desk_chooses_the_products_sees_the_total_and_records_the_sale()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var empty = await GetAsync(desk, Path);
        empty.ShouldContain("Sell at the counter");
        empty.ShouldContain("Shampoo 250 ml, £12.50 (on the shelf: 8)");
        empty.ShouldNotContain("data-testid=\"sale-quote\"");

        var chosen = $"{Path}?rows[0].Product={_shampoo}&rows[0].Quantity=2&rows[1].Product=&rows[1].Quantity=&rows[2].Product={_oil}&rows[2].Quantity=";
        var quoted = await GetAsync(desk, chosen);
        quoted.ShouldContain("data-testid=\"sale-total\">£43.00<");
        quoted.ShouldContain("2 × £12.50");
        quoted.ShouldContain("1 × £18.00");
        await InScopeAsync(_studio.Id, async db => (await db.ClientAccountEntries.AsNoTracking().AnyAsync()).ShouldBeFalse("looking records nothing"));

        var response = await desk.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(quoted)),
            new("rows[0].Product", _shampoo.ToString()),
            new("rows[0].Quantity", "2"),
            new("rows[1].Product", _oil.ToString()),
            new("rows[1].Quantity", "1"),
            new("method", "Cash"),
        ]));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.ShouldBe(Path);
        var after = await GetAsync(desk, Path);
        after.ShouldContain("data-testid=\"flash\" class=\"notice notice-ok\">Sold for £43.00 (Cash).<");
        after.ShouldContain("Shampoo 250 ml, £12.50 (on the shelf: 6)");
        await InScopeAsync(_studio.Id, async db => (await db.ClientAccountEntries.AsNoTracking().SingleAsync()).ByUserId.ShouldNotBeNull());
    }

    [Fact]
    public async Task A_wrong_choice_is_explained_on_the_page_and_a_post_needs_its_token()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        (await GetAsync(desk, $"{Path}?rows[0].Product={_shampoo}&rows[0].Quantity=500")).ShouldContain("data-testid=\"sale-problem\" class=\"notice notice-error\">The quantity must be between 1 and 99.<");
        var form = await GetAsync(desk, Path);
        var nothing = await desk.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent(
        [
            new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(await GetAsync(desk, $"{Path}?rows[0].Product={_shampoo}"))),
            new("method", "Cash"),
        ]));
        nothing.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(desk, Path)).ShouldContain("Choose at least one product.");
        form.ShouldNotContain("sale-problem");

        var noToken = await desk.PostAsync(new Uri(Path, UriKind.Relative), new FormUrlEncodedContent([new("rows[0].Product", _shampoo.ToString()), new("method", "Cash")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await InScopeAsync(_studio.Id, async db => (await db.ClientAccountEntries.AsNoTracking().AnyAsync()).ShouldBeFalse());
    }

    [Fact]
    public async Task Whoever_takes_payments_gets_in_and_another_salon_sees_none_of_it()
    {
        await SalesAsync(_studio.Id, s => s.SellAsync([(_shampoo, 1)], PaymentMethod.Cash, null));
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var anonymous = factory.ClientFor(HostA);
        using var polish = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var denied = await staff.GetAsync(new Uri(Path, UriKind.Relative));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        (await anonymous.GetAsync(new Uri(Path, UriKind.Relative))).Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await GetAsync(staff, "/admin/products")).ShouldNotContain("data-testid=\"sell-at-counter\"");

        var theirs = await GetAsync(polish, Path);
        theirs.ShouldNotContain("Shampoo 250 ml");
        theirs.ShouldContain("data-testid=\"nothing-to-sell\"");
        var report = await ScopedAsync<IReportService, SalonReport>(_polish.Id, s => s.GetAsync(ReportPreset.ThisMonth, null, null));
        (report.Takings.Total.Amount, report.Retail.CounterSales).ShouldBe((0m, 0));
    }

    private async Task<CounterSaleRefusal> RefusalAsync(IReadOnlyList<(Guid, int)> lines)
        => (await Should.ThrowAsync<CounterSaleException>(() => SalesAsync(_studio.Id, s => s.SellAsync(lines, PaymentMethod.Cash, null)))).Refusal;

    private Task<T> SalesAsync<T>(Guid tenantId, Func<ICounterSaleService, Task<T>> act) => ScopedAsync(tenantId, act);

    private async Task<T> ScopedAsync<TService, T>(Guid tenantId, Func<TService, Task<T>> act)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task InScopeAsync(Guid tenantId, Func<SalonDbContext, Task> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await act(scope.ServiceProvider.GetRequiredService<SalonDbContext>());
    }

    private async Task<Guid> ProductAsync(Guid tenantId, string name, decimal? price, decimal? cost, int onHand, bool active = true, string currency = "GBP")
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, CostPrice = cost, Currency = currency, StockOnHand = onHand, IsActive = active };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}