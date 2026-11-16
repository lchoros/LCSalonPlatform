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
/// Returning a product that was paid for (ADR 0040): the money goes back as its own line that points at the sale, at
/// the price the product was rung up at; the bill line counts what came back; the unit goes back on its shelf only
/// when it can be sold again; and nobody returns more than the client still has. The shampoo costs £12.50.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class ProductReturnsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
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

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
        _shampoo = await ProductAsync("Shampoo 250 ml", 12.50m, onHand: 8);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task One_bottle_comes_back_for_the_shelf_and_two_more_that_cannot_be_sold_again()
    {
        var (visit, line) = await SoldAsync(3, PaymentMethod.CardAtSalonTerminal);
        (await OnHandAsync()).ShouldBe(5);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var bill = await GetAsync(desk, CheckoutPath(visit));
        bill.ShouldContain("data-testid=\"return-product\"");
        bill.ShouldContain("max=\"3\"", Case.Sensitive, "no more than the client has");
        bill.ShouldNotContain("data-testid=\"bill-product-returned\"");

        // 1. One unopened bottle: cash back, and it goes on the shelf again.
        var first = await ReturnAsync(desk, visit, line, "1", "Cash", backOnShelf: true);
        first.ShouldContain("data-testid=\"flash\" class=\"notice notice-ok\">The return has been recorded.<");
        first.ShouldContain("data-testid=\"bill-product-returned\">Returned: 1<");
        first.ShouldContain("max=\"2\"");
        var entries = await EntriesAsync();
        entries.Select(e => (e.Kind, e.Amount, e.Method)).ShouldBe(
        [
            (ClientAccountEntryKind.VisitPayment, 20m, PaymentMethod.CardAtSalonTerminal),
            (ClientAccountEntryKind.ProductSale, 37.50m, PaymentMethod.CardAtSalonTerminal),
            (ClientAccountEntryKind.ProductReturn, 12.50m, PaymentMethod.Cash),
        ]);
        var back = entries[2];
        back.RelatedEntryId.ShouldBe(entries[1].Id, "the return points at the sale it undoes in part");
        back.AppointmentId.ShouldBe(visit);
        back.ClientId.ShouldBe(_clientId);
        back.ByUserId.ShouldNotBeNull();
        back.ProviderReference.ShouldNotBeNull().ShouldStartWith("manual:refund:");
        (await LineAsync(line)).ReturnedQuantity.ShouldBe(1);
        (await OnHandAsync()).ShouldBe(6);
        var shelf = (await MovementsAsync()).Last();
        shelf.Kind.ShouldBe(StockMovementKind.Returned);
        shelf.Quantity.ShouldBe(1);
        shelf.AppointmentId.ShouldBe(visit);

        // 2. Two opened bottles: the money goes back, the shelf does not get them.
        var second = await ReturnAsync(desk, visit, line, "2", "Cash", backOnShelf: false);
        second.ShouldContain("data-testid=\"bill-product-returned\">Returned: 3<");
        second.ShouldNotContain("data-testid=\"return-product\"", Case.Sensitive, "nothing is left to bring back");
        (await LineAsync(line)).ReturnedQuantity.ShouldBe(3);
        (await OnHandAsync()).ShouldBe(6, "an opened bottle does not come back to the shelf");
        (await EntriesAsync()).Last().Amount.ShouldBe(25m);

        // 3. The till: the card took the visit and the sale, the cash drawer gave £37.50 back.
        var day = await TillAsync(s => s.GetDayAsync(null));
        day.Takings.Amount.ShouldBe(20m, "the visit is all that stays");
        day.ExpectedCash.Amount.ShouldBe(-37.50m);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        (await GetAsync(owner, "/admin/payments/cash-up")).ShouldContain("Products returned");
        (await GetAsync(owner, "/admin/reports")).ShouldContain("Products returned");

        // 4. The card no longer lists a product the client gave back in full; the shelf's history names the return.
        (await GetAsync(desk, $"/admin/clients/{_clientId}")).ShouldNotContain("data-testid=\"client-purchases\"");
        (await GetAsync(owner, $"/admin/products/{_shampoo}")).ShouldContain("data-testid=\"line-kind\">Returned by a client<");
    }

    [Fact]
    public async Task The_money_goes_back_at_the_price_the_product_was_rung_up_at()
    {
        var (visit, line) = await SoldAsync(2, PaymentMethod.Cash);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products
                .Where(p => p.Id == _shampoo).ExecuteUpdateAsync(s => s.SetProperty(p => p.RetailPrice, 15m));
        }

        var bill = await AccountAsync(s => s.ReturnProductAsync(visit, line, 1, PaymentMethod.Cash, true, null));

        (await EntriesAsync()).Last().Amount.ShouldBe(12.50m);
        var shown = bill.Products.ShouldHaveSingleItem();
        shown.Returned.ShouldBe(1);
        shown.Kept.ShouldBe(1);
        (await AccountAsync(s => s.GetPurchasesAsync(_clientId))).ShouldHaveSingleItem().Quantity.ShouldBe(1, "the card shows what the client still has");
    }

    [Fact]
    public async Task What_cannot_be_a_return_is_refused_and_changes_nothing()
    {
        var (visit, line) = await SoldAsync(2, PaymentMethod.Cash);
        var other = await VisitAsync(Now - Duration.FromMinutes(90), AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(other, _shampoo, 1, null));
        var unpaid = (await LinesAsync(other)).ShouldHaveSingleItem().Id;

        (await RefusalAsync(() => AccountAsync(s => s.ReturnProductAsync(other, unpaid, 1, PaymentMethod.Cash, true, null)))).ShouldBe(ClientAccountRefusal.ProductLineNotPaid);
        (await RefusalAsync(() => AccountAsync(s => s.ReturnProductAsync(visit, line, 0, PaymentMethod.Cash, true, null)))).ShouldBe(ClientAccountRefusal.ReturnMoreThanKept);
        (await RefusalAsync(() => AccountAsync(s => s.ReturnProductAsync(visit, line, 3, PaymentMethod.Cash, true, null)))).ShouldBe(ClientAccountRefusal.ReturnMoreThanKept);
        (await RefusalAsync(() => AccountAsync(s => s.ReturnProductAsync(visit, Guid.NewGuid(), 1, PaymentMethod.Cash, true, null)))).ShouldBe(ClientAccountRefusal.NotFound);
        (await RefusalAsync(() => AccountAsync(s => s.ReturnProductAsync(other, line, 1, PaymentMethod.Cash, true, null)))).ShouldBe(ClientAccountRefusal.NotFound, "a line belongs to its own bill");

        await AccountAsync(s => s.ReturnProductAsync(visit, line, 2, PaymentMethod.Cash, true, null));
        (await RefusalAsync(() => AccountAsync(s => s.ReturnProductAsync(visit, line, 1, PaymentMethod.Cash, true, null)))).ShouldBe(ClientAccountRefusal.ReturnMoreThanKept, "everything is back already");

        (await EntriesAsync()).Count(e => e.Kind == ClientAccountEntryKind.ProductReturn).ShouldBe(1);
        (await LineAsync(line)).ReturnedQuantity.ShouldBe(2);
        (await OnHandAsync()).ShouldBe(8, "two sold, two back");
    }

    [Fact]
    public async Task A_refused_return_comes_back_to_the_bill_with_the_reason()
    {
        var (visit, line) = await SoldAsync(1, PaymentMethod.Cash);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        (await ReturnAsync(desk, visit, line, "5", "Cash", backOnShelf: true)).ShouldContain("data-testid=\"error\" class=\"notice notice-error\">The client does not have that many of this product left. Check how many came back.<");
        (await ReturnAsync(desk, visit, line, "1", "Barter", backOnShelf: true)).ShouldContain("Choose how it was paid.");
        (await LineAsync(line)).ReturnedQuantity.ShouldBe(0);
        (await EntriesAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_return_needs_the_checkout_permission_a_sign_in_the_salon_and_a_token()
    {
        var (visit, line) = await SoldAsync(1, PaymentMethod.Cash);
        var path = $"{CheckoutPath(visit)}/products/{line}/return";
        static List<KeyValuePair<string, string>> Fields() => [new("quantity", "1"), new("method", "Cash"), new("backOnShelf", "true")];
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var anonymous = factory.ClientFor(HostA);
        using var other = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);

        var denied = await PostAsync(staff, path, Fields(), await GetAsync(staff, "/admin/products"));
        denied.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var login = await anonymous.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(Fields()));
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        login.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/login");
        (await PostAsync(other, path, Fields(), await GetAsync(other, "/admin/products"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await desk.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(Fields()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await LineAsync(line)).ReturnedQuantity.ShouldBe(0);
        (await OnHandAsync()).ShouldBe(7);
    }

    [Fact]
    public async Task The_database_itself_refuses_more_returned_than_bought()
    {
        var (_, line) = await SoldAsync(2, PaymentMethod.Cash);
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();

        var row = await db.VisitProductLines.SingleAsync(l => l.Id == line);
        row.ReturnedQuantity = 3;

        (await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync())).InnerException.ShouldNotBeNull().Message.ShouldContain("ck_visit_product_lines_returned_within_quantity");
    }

    private static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    /// <summary>An arrived visit with the shampoo on its bill, paid: the visit and the paid bill line.</summary>
    private async Task<(Guid Visit, Guid Line)> SoldAsync(int quantity, PaymentMethod method)
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(visit, _shampoo, quantity, null));
        await AccountAsync(s => s.PayBillAsync(visit, method, [], null));
        return (visit, (await LinesAsync(visit)).ShouldHaveSingleItem().Id);
    }

    /// <summary>Posts the return form of a paid line and returns the bill it lands on, decoded.</summary>
    private static async Task<string> ReturnAsync(HttpClient client, Guid visit, Guid line, string quantity, string method, bool backOnShelf)
    {
        var page = await GetAsync(client, CheckoutPath(visit));
        List<KeyValuePair<string, string>> fields = [new("quantity", quantity), new("method", method)];
        if (backOnShelf)
        {
            // An unticked box posts nothing.
            fields.Add(new("backOnShelf", "true"));
        }

        var response = await PostAsync(client, $"{CheckoutPath(visit)}/products/{line}/return", fields, page);
        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(CheckoutPath(visit));
        return await GetAsync(client, CheckoutPath(visit));
    }

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);

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

    private async Task<int> OnHandAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Products.AsNoTracking().Where(p => p.Id == _shampoo).Select(p => p.StockOnHand).SingleAsync();
    }

    private async Task<List<StockMovement>> MovementsAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StockMovements.AsNoTracking()
            .Where(m => m.ProductId == _shampoo).OrderBy(m => m.At).ThenBy(m => m.Id).ToListAsync();
    }

    private async Task<List<VisitProductLine>> LinesAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().VisitProductLines.AsNoTracking().Where(l => l.AppointmentId == appointmentId).ToListAsync();
    }

    private async Task<VisitProductLine> LineAsync(Guid lineId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().VisitProductLines.AsNoTracking().SingleAsync(l => l.Id == lineId);
    }

    private async Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
    }

    private async Task<T> TillAsync<T>(Func<ICashUpService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<ICashUpService>());
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
}