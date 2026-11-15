using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// Products on a visit's bill (ADR 0037): the desk rings a product up at the checkout, the bill adds it to what is
/// left of the visit, one payment settles both, the money goes on the ledger as its own line and every unit leaves
/// its shelf in the same step. An unpaid line can be changed and taken off; a paid one never. The visit in these
/// tests costs £20.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class BillProductsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private Guid _shampoo;
    private Guid _oil;
    private Guid _colour;
    private Guid _oldSpray;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _shampoo = await ProductAsync(_studio.Id, "Shampoo 250 ml", 12.50m, onHand: 8);
        _oil = await ProductAsync(_studio.Id, "Hair oil 100 ml", 18m, onHand: 5);
        _colour = await ProductAsync(_studio.Id, "Colour tube 6.0", null, onHand: 10);
        _oldSpray = await ProductAsync(_studio.Id, "Old hairspray", 9m, onHand: 1, active: false);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_desk_rings_products_up_changes_its_mind_and_one_payment_settles_the_visit_and_the_products()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var bill = await GetAsync(desk, CheckoutPath(visit));
        bill.ShouldContain("data-testid=\"add-product-form\"");
        Options(bill).ShouldBe(["Hair oil 100 ml · £18.00", "Shampoo 250 ml · £12.50"], "only products in use with a price, by name");
        bill.ShouldNotContain("data-testid=\"bill-products\"");

        var added = await AddAsync(desk, visit, _shampoo, "2");
        added.ShouldContain("data-testid=\"flash\" class=\"notice notice-ok\">“Shampoo 250 ml” is on the bill.<");
        added.ShouldContain("data-testid=\"bill-product-name\">Shampoo 250 ml × 2<");
        added.ShouldContain("data-testid=\"bill-product-total\">£25.00<");
        added.ShouldContain("data-testid=\"bill-outstanding\">£45.00<");
        (await OnHandAsync(_shampoo)).ShouldBe(8, "nothing leaves the shelf until the bill is paid");
        (await EntriesAsync()).ShouldBeEmpty();

        // The same product again raises the line; another product gets its own; taking one off is just that.
        (await AddAsync(desk, visit, _shampoo, "1")).ShouldContain("data-testid=\"bill-product-name\">Shampoo 250 ml × 3<");
        var two = await AddAsync(desk, visit, _oil, "1");
        Regex.Count(two, "data-testid=\"bill-product\"").ShouldBe(2);
        two.ShouldContain("data-testid=\"bill-outstanding\">£75.50<");
        var oilLine = (await LinesAsync(visit)).Single(l => l.ProductId == _oil);
        var removed = await PostAsync(desk, $"{CheckoutPath(visit)}/products/{oilLine.Id}/remove", [], two);
        removed.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var one = await GetAsync(desk, CheckoutPath(visit));
        one.ShouldContain("The product has been taken off the bill.");
        one.ShouldContain("data-testid=\"bill-outstanding\">£57.50<");

        var paid = await PostAsync(desk, CheckoutPath(visit), [new("method", "Cash")], one);
        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var entries = await EntriesAsync();
        entries.Select(e => (e.Kind, e.Amount, e.Method)).ShouldBe(
        [
            (ClientAccountEntryKind.VisitPayment, 20m, PaymentMethod.Cash),
            (ClientAccountEntryKind.ProductSale, 37.50m, PaymentMethod.Cash),
        ]);
        entries.ShouldAllBe(e => e.AppointmentId == visit && e.ClientId == _clientId && e.Currency == "GBP" && e.ByUserId != null && e.ProviderReference != null);
        var line = (await LinesAsync(visit)).ShouldHaveSingleItem();
        line.PaymentEntryId.ShouldBe(entries[1].Id);
        line.Quantity.ShouldBe(3);
        line.UnitPrice.ShouldBe(12.50m);
        (await OnHandAsync(_shampoo)).ShouldBe(5);
        (await OnHandAsync(_oil)).ShouldBe(5, "a product taken off the bill never left its shelf");
        var sold = (await MovementsAsync(_shampoo)).Last();
        sold.Kind.ShouldBe(StockMovementKind.Sold);
        sold.Quantity.ShouldBe(-3);
        sold.OnHandAfter.ShouldBe(5);
        sold.AppointmentId.ShouldBe(visit);
        sold.ByUserId.ShouldNotBeNull();

        var after = await GetAsync(desk, CheckoutPath(visit));
        after.ShouldContain("The payment has been recorded.");
        after.ShouldContain("data-testid=\"bill\" data-status=\"Completed\"");
        after.ShouldContain("data-testid=\"bill-product\" data-paid=\"true\"");
        after.ShouldContain("data-testid=\"bill-product-paid\">Paid: Cash<");
        after.ShouldContain("data-testid=\"bill-outstanding\">£0.00<");
        after.ShouldNotContain("data-testid=\"remove-product\"", Case.Sensitive, "a paid line stays");
        after.ShouldContain("This bill is paid.");
    }

    [Fact]
    public async Task The_till_and_the_report_show_products_sold_apart_from_visits()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.AddProductAsync(visit, _shampoo, 2, null));
        await AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.CardAtSalonTerminal, [], null));
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);

        var till = await GetAsync(owner, "/admin/payments/cash-up");
        till.ShouldContain("Products sold");
        till.ShouldContain("Visit paid");
        till.ShouldContain("£45.00", Case.Sensitive, "the day's takings are the visit and the products together");

        var report = await GetAsync(owner, "/admin/reports");
        report.ShouldContain("Products sold");
        report.ShouldContain("£25.00");
        report.ShouldContain("data-testid=\"headline-unpaid\"");
        report.ShouldNotContain("data-testid=\"unpaid-checkout\"", Case.Sensitive, "the visit is paid; products do not make it look unpaid");
    }

    [Fact]
    public async Task A_sale_from_a_shelf_that_reads_too_low_counts_it_up_first_and_never_goes_below_zero()
    {
        var wax = await ProductAsync(_studio.Id, "Styling wax 75 ml", 9.50m, onHand: 1);
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.AddProductAsync(visit, wax, 3, null));

        await AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));

        // The client walked out with three, so three were there: the record is put right, then the sale comes off.
        var movements = await MovementsAsync(wax);
        movements.Select(m => (m.Kind, m.Quantity, m.OnHandAfter, m.AppointmentId)).ShouldBe(
        [
            (StockMovementKind.Counted, 1, 1, null),
            (StockMovementKind.Counted, 2, 3, visit),
            (StockMovementKind.Sold, -3, 0, visit),
        ]);
        (await OnHandAsync(wax)).ShouldBe(0);
        StockRules.OnHand(movements.Select(m => m.Quantity)).ShouldBe(0);

        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, $"/admin/products/{wax}");
        page.ShouldContain("data-testid=\"line-kind\">Sold<");
        page.ShouldContain("data-testid=\"line-kind\">Found at a sale<");
        page.IndexOf(">Sold<", StringComparison.Ordinal).ShouldBeLessThan(page.IndexOf(">Found at a sale<", StringComparison.Ordinal), "newest first: the sale after the count");
    }

    [Fact]
    public async Task A_bottle_bought_after_the_visit_was_paid_is_a_second_payment_and_the_visit_is_not_paid_twice()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var bill = await GetAsync(desk, CheckoutPath(visit));
        (await PostAsync(desk, CheckoutPath(visit), [new("method", "CardAtSalonTerminal")], bill)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var later = await AddAsync(desk, visit, _oil, "1");
        later.ShouldContain("data-testid=\"bill-outstanding\">£18.00<");
        later.ShouldContain("data-testid=\"pay-bill-form\"");
        (await PostAsync(desk, CheckoutPath(visit), [new("method", "Cash")], later)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var again = await AddAsync(desk, visit, _shampoo, "1");
        (await PostAsync(desk, CheckoutPath(visit), [new("method", "Cash")], again)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        (await EntriesAsync()).Select(e => (e.Kind, e.Amount, e.Method)).ShouldBe(
        [
            (ClientAccountEntryKind.VisitPayment, 20m, PaymentMethod.CardAtSalonTerminal),
            (ClientAccountEntryKind.ProductSale, 18m, PaymentMethod.Cash),
            (ClientAccountEntryKind.ProductSale, 12.50m, PaymentMethod.Cash),
        ]);
        var lines = await LinesAsync(visit);
        lines.Count.ShouldBe(2);
        lines.Select(l => l.PaymentEntryId).Distinct().Count().ShouldBe(2, "each line points at the payment that settled it");
        (await OnHandAsync(_oil)).ShouldBe(4);
        (await OnHandAsync(_shampoo)).ShouldBe(7);
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Completed);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null)))).ShouldBe(ClientAccountRefusal.NothingToPay);
    }

    [Fact]
    public async Task A_bill_keeps_the_name_and_the_price_a_product_had_when_it_was_rung_up()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.AddProductAsync(visit, _shampoo, 1, null));
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products
                .Where(p => p.Id == _shampoo)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "Shampoo 300 ml").SetProperty(p => p.RetailPrice, 15m).SetProperty(p => p.IsActive, false));
        }

        var bill = await AccountAsync(_studio.Id, s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));

        var line = bill.Products.ShouldHaveSingleItem();
        line.Name.ShouldBe("Shampoo 250 ml");
        line.UnitPrice.Amount.ShouldBe(12.50m);
        line.IsPaid.ShouldBeTrue();
        line.PaidWith.ShouldBe(PaymentMethod.Cash);
        (await EntriesAsync()).Single(e => e.Kind == ClientAccountEntryKind.ProductSale).Amount.ShouldBe(12.50m, "a product rung up is sold at the price it was rung up at");
    }

    [Fact]
    public async Task What_cannot_go_on_a_bill_is_refused_and_changes_nothing()
    {
        var arrived = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        var booked = await VisitAsync(Now + Duration.FromDays(1), AppointmentStatus.Booked);
        var polishProduct = await ProductAsync(_polish.Id, "Szampon", 30m, onHand: 3);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(booked, _shampoo, 1, null)))).ShouldBe(ClientAccountRefusal.VisitNotPayable);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(Guid.NewGuid(), _shampoo, 1, null)))).ShouldBe(ClientAccountRefusal.NotFound);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, _colour, 1, null)))).ShouldBe(ClientAccountRefusal.ProductNotForSale, "no price: salon use only");
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, _oldSpray, 1, null)))).ShouldBe(ClientAccountRefusal.ProductNotForSale, "no longer used");
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, polishProduct, 1, null)))).ShouldBe(ClientAccountRefusal.ProductNotForSale, "another salon's product");
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, Guid.NewGuid(), 1, null)))).ShouldBe(ClientAccountRefusal.ProductNotForSale);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, _shampoo, 0, null)))).ShouldBe(ClientAccountRefusal.QuantityOutOfRange);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, _shampoo, 100, null)))).ShouldBe(ClientAccountRefusal.QuantityOutOfRange);
        (await LinesAsync(arrived)).ShouldBeEmpty();

        await AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, _shampoo, 60, null));
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.AddProductAsync(arrived, _shampoo, 40, null)))).ShouldBe(ClientAccountRefusal.QuantityOutOfRange, "one line holds at most 99");
        var line = (await LinesAsync(arrived)).ShouldHaveSingleItem();
        line.Quantity.ShouldBe(60);

        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RemoveProductAsync(arrived, Guid.NewGuid())))).ShouldBe(ClientAccountRefusal.NotFound);
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RemoveProductAsync(booked, line.Id)))).ShouldBe(ClientAccountRefusal.NotFound, "a line belongs to its own bill");
        await AccountAsync(_studio.Id, s => s.PayBillAsync(arrived, PaymentMethod.Cash, [], null));
        (await RefusalAsync(() => AccountAsync(_studio.Id, s => s.RemoveProductAsync(arrived, line.Id)))).ShouldBe(ClientAccountRefusal.ProductLinePaid);
        (await LinesAsync(arrived)).ShouldHaveSingleItem().IsPaid.ShouldBeTrue();
        (await EntriesAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_refused_product_comes_back_to_the_bill_with_the_reason()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        (await AddAsync(desk, visit, _colour, "1")).ShouldContain("data-testid=\"error\" class=\"notice notice-error\">This product cannot be sold: it has no price or the salon no longer uses it.<");
        (await AddAsync(desk, visit, _shampoo, "0")).ShouldContain("Enter how many, from 1 to 99.");
        (await AddAsync(desk, visit, _shampoo, "abc")).ShouldContain("Enter how many, from 1 to 99.");
        (await LinesAsync(visit)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Products_are_offered_only_on_a_bill_that_can_be_paid_and_only_when_the_salon_sells_some()
    {
        var booked = await VisitAsync(Now + Duration.FromDays(1), AppointmentStatus.Booked);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await GetAsync(desk, CheckoutPath(booked))).ShouldNotContain("data-testid=\"add-product-step\"");

        await factory.CreateUserAsync(_polish.Id, "desk@salon-pl.localhost", Role.Receptionist);
        var (staff, service) = await VisitSeed.SalonAsync(factory, _polish.Id);
        var client = await VisitSeed.ClientAsync(factory, _polish.Id, "Anna", "Nowak", "601 234 567");
        var polishVisit = await VisitSeed.AppointmentAsync(factory, _polish.Id, client, staff, service, Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var polishDesk = factory.ClientFor(HostB);
        (await IdentityTestClient.LoginAsync(polishDesk, "desk@salon-pl.localhost", IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);

        var bill = await GetAsync(polishDesk, CheckoutPath(polishVisit));
        bill.ShouldContain("data-testid=\"pay-bill-form\"");
        bill.ShouldNotContain("data-testid=\"add-product-step\"", Case.Sensitive, "a salon that sells nothing is not shown an empty form");
    }

    [Fact]
    public async Task The_product_forms_need_the_checkout_permission_a_sign_in_the_salon_and_a_token()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.AddProductAsync(visit, _shampoo, 1, null));
        var line = (await LinesAsync(visit)).ShouldHaveSingleItem();
        var addPath = $"{CheckoutPath(visit)}/products";
        var removePath = $"{CheckoutPath(visit)}/products/{line.Id}/remove";
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var page = await GetAsync(desk, CheckoutPath(visit));

        // Staff work the diary and may see the shelves, but taking money is not theirs.
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var staffToken = await GetAsync(staff, "/admin/products");
        foreach (var path in new[] { addPath, removePath })
        {
            var denied = await PostAsync(staff, path, [new("product", _shampoo.ToString()), new("quantity", "1")], staffToken);
            denied.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied", path);

            using var anonymous = factory.ClientFor(HostA);
            var login = await anonymous.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new("quantity", "1")]));
            login.StatusCode.ShouldBe(HttpStatusCode.Redirect, path);
            login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login", path);

            (await desk.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new("product", _shampoo.ToString()), new("quantity", "1")]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest, path);
        }

        // Another salon's desk gets a 404 for this salon's visit and line, like for ids that do not exist.
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var otherToken = await GetAsync(other, "/admin/products");
        (await PostAsync(other, addPath, [new("product", _shampoo.ToString()), new("quantity", "1")], otherToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(other, removePath, [], otherToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await PostAsync(desk, $"{CheckoutPath(Guid.NewGuid())}/products", [new("product", _shampoo.ToString()), new("quantity", "1")], page)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var still = (await LinesAsync(visit)).ShouldHaveSingleItem();
        still.Quantity.ShouldBe(1);
        still.IsPaid.ShouldBeFalse();
    }

    [Fact]
    public async Task The_database_itself_keeps_one_unpaid_line_per_product_and_refuses_an_empty_one()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(_studio.Id, s => s.AddProductAsync(visit, _shampoo, 1, null));
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();

        db.VisitProductLines.Add(new VisitProductLine { AppointmentId = visit, ProductId = _shampoo, ProductName = "Shampoo 250 ml", Quantity = 1, UnitPrice = 12.50m, Currency = "GBP", AddedAt = Now });
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ux_visit_product_lines_one_unpaid_line_per_product");
        db.ChangeTracker.Clear();

        db.VisitProductLines.Add(new VisitProductLine { AppointmentId = visit, ProductId = _oil, ProductName = "Hair oil 100 ml", Quantity = 0, UnitPrice = 18m, Currency = "GBP", AddedAt = Now });
        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ck_visit_product_lines_quantity_positive");
    }

    private static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    private static List<string> Options(string html)
        => OptionPattern().Matches(html).Select(m => m.Groups["text"].Value).ToList();

    /// <summary>Posts the "add a product" form of the visit's bill and returns the bill it lands on, decoded.</summary>
    private static async Task<string> AddAsync(HttpClient client, Guid visit, Guid product, string quantity)
    {
        var page = await GetAsync(client, CheckoutPath(visit));
        var response = await PostAsync(client, $"{CheckoutPath(visit)}/products", [new("product", product.ToString()), new("quantity", quantity)], page);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(CheckoutPath(visit));
        return await GetAsync(client, CheckoutPath(visit));
    }

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);

    /// <summary>A product on the shelf the way the app puts it there: the row, then a first count through the service.</summary>
    private async Task<Guid> ProductAsync(Guid tenantId, string name, decimal? price, int onHand, bool active = true)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == tenantId);
        var product = new Product { Name = name, RetailPrice = price, Currency = tenant.Currency, IsActive = active };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, onHand, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        return product.Id;
    }

    private async Task<int> OnHandAsync(Guid productId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products.AsNoTracking().Where(p => p.Id == productId).Select(p => p.StockOnHand).SingleAsync();
    }

    private async Task<List<StockMovement>> MovementsAsync(Guid productId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId).OrderBy(m => m.At).ThenBy(m => m.Id).ToListAsync();
    }

    private async Task<List<VisitProductLine>> LinesAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().VisitProductLines.AsNoTracking()
            .Where(l => l.AppointmentId == appointmentId).OrderBy(l => l.AddedAt).ThenBy(l => l.Id).ToListAsync();
    }

    private async Task<AppointmentStatus> StatusAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync();
    }

    private async Task<T> AccountAsync<T>(Guid tenantId, Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
    }

    private static async Task<ClientAccountRefusal> RefusalAsync(Func<Task> act)
        => (await Should.ThrowAsync<ClientAccountException>(act)).Refusal;

    private async Task<List<ClientAccountEntry>> EntriesAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync();
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, List<KeyValuePair<string, string>> fields, string pageWithToken)
    {
        fields.Add(new("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)));
        return await client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    [GeneratedRegex("<option value=\"[0-9a-f-]{36}\">(?<text>[^<]+)</option>")]
    private static partial Regex OptionPattern();
}