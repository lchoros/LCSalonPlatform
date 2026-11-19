using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Locations;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// The receipt of a paid visit (ADR 0070): what the client had and how it was paid, read from the bill, on the desk's
/// screen and by email to the client. Olivia's cut costs £20; she paid £5 by card at the terminal first, took two bottles of shampoo
/// at £12.50 and brought one back, and paid the rest in cash.
/// </summary>
[Collection(SharedDatabase.Name)]
public class ReceiptTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const string OliviaEmail = "olivia@example.com";

    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _olivia;
    private Guid _shampoo;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        factory.Emails.Clear();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        _polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _olivia = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123", OliviaEmail);
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var shampoo = new Product { Name = "Shampoo 250 ml", RetailPrice = 12.50m, Currency = "GBP", IsActive = true };
        db.Products.Add(shampoo);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(shampoo.Id, StockMovementKind.Counted, 8, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        _shampoo = shampoo.Id;
        await scope.ServiceProvider.GetRequiredService<ILocationService>().SaveMainAsync(new LocationDetails(null, "20 Drake Street", null, "Rochdale", "OL16 1NT", "01632 960020", null));
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_desk_opens_the_receipt_of_a_paid_visit_and_emails_it_to_the_client()
    {
        var visit = await PaidVisitAsync(_olivia);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        // The paid bill links to its receipt.
        (await GetAsync(desk, $"/admin/payments/checkout/{visit}")).ShouldContain($"href=\"/admin/payments/receipt/{visit}\" data-testid=\"receipt-link\"");

        var page = await GetAsync(desk, $"/admin/payments/receipt/{visit}");
        page.ShouldContain("data-testid=\"receipt-salon\">Studio 20<");
        page.ShouldContain("20 Drake Street");
        page.ShouldContain("data-testid=\"receipt-client\">Olivia Brown<");
        page.ShouldContain("with Anna");
        page.ShouldContain("data-testid=\"receipt-price\">£20.00<");
        page.ShouldContain("data-testid=\"receipt-product\">1 × Shampoo 250 ml<", Case.Sensitive, "the bottle brought back is not on it");
        page.ShouldContain("data-testid=\"receipt-total\">£32.50<");
        page.ShouldContain("data-kind=\"Part\">Paid earlier: Card at the salon's terminal<");
        page.ShouldContain("data-kind=\"Visit\">Paid: Cash<");
        page.ShouldContain("data-kind=\"Products\">Products paid: Cash<");
        page.ShouldContain("This receipt covers this visit and the products bought on it.");
        page.ShouldContain($"Email it to {OliviaEmail}");

        var sent = await PostAsync(desk, $"/admin/payments/receipt/{visit}/email", page);
        sent.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        sent.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/admin/payments/receipt/{visit}");
        (await GetAsync(desk, $"/admin/payments/receipt/{visit}")).ShouldContain("The receipt is on its way to the client.");

        var email = factory.Emails.Messages.ShouldHaveSingleItem();
        email.To.ShouldBe(OliviaEmail);
        email.Subject.ShouldStartWith("Your receipt from Studio 20, ");
        var text = email.TextBody.ShouldNotBeNull();
        text.ShouldStartWith("Thank you for your visit, Olivia. Here is your receipt.");
        text.ShouldContain("Cut: £20.00\n1 × Shampoo 250 ml: £12.50\nTotal: £32.50");
        text.ShouldContain("- Paid earlier: Card at the salon's terminal — £5.00\n- Paid: Cash — £15.00\n- Products paid: Cash — £12.50");
        text.ShouldContain("20 Drake Street");
        email.HtmlBody.ShouldContain("Shampoo 250 ml");
    }

    [Fact]
    public async Task A_visit_not_paid_yet_has_no_receipt_and_the_other_salon_and_staff_never_see_one()
    {
        var paid = await PaidVisitAsync(_olivia);
        var arrived = await VisitSeed.AppointmentAsync(factory, _studio.Id, _olivia, _staffId, _serviceId, Now - Duration.FromHours(2), AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        (await desk.GetAsync(new Uri($"/admin/payments/receipt/{arrived}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetAsync(desk, $"/admin/payments/checkout/{arrived}")).ShouldNotContain("data-testid=\"receipt-link\"");

        using var theirs = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        (await theirs.GetAsync(new Uri($"/admin/payments/receipt/{paid}", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var theirPage = await GetAsync(theirs, "/admin/settings/waitlist");
        (await PostAsync(theirs, $"/admin/payments/receipt/{paid}/email", theirPage)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var denied = await staff.GetAsync(new Uri($"/admin/payments/receipt/{paid}", UriKind.Relative));
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        factory.Emails.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_email_goes_to_a_client_without_an_address_or_when_the_salon_sends_no_emails()
    {
        var noAddress = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Adams", "+447700900111");
        var noahs = await PaidVisitAsync(noAddress);
        var olivias = await PaidVisitAsync(_olivia, hoursEarlier: 2);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var page = await GetAsync(desk, $"/admin/payments/receipt/{noahs}");
        page.ShouldNotContain("data-testid=\"email-receipt\"");
        page.ShouldContain("data-testid=\"no-email\"");
        await PostAsync(desk, $"/admin/payments/receipt/{noahs}/email", page);
        (await GetAsync(desk, $"/admin/payments/receipt/{noahs}")).ShouldContain("The client's card has no email address.");

        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            var settings = scope.ServiceProvider.GetRequiredService<ISettingService>();
            var notifications = await settings.LoadAsync<NotificationSettings>();
            notifications.EmailEnabled = false;
            await settings.SaveAsync(notifications);
        }

        page = await GetAsync(desk, $"/admin/payments/receipt/{olivias}");
        await PostAsync(desk, $"/admin/payments/receipt/{olivias}/email", page);
        (await GetAsync(desk, $"/admin/payments/receipt/{olivias}")).ShouldContain("Emails to clients are switched off");
        factory.Emails.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_payments_on_a_receipt_add_up_to_its_total()
    {
        var visit = await PaidVisitAsync(_olivia);
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var receipt = (await scope.ServiceProvider.GetRequiredService<IReceiptService>().GetAsync(visit)).ShouldNotBeNull();

        receipt.Total.ShouldBe(receipt.Paid);
        receipt.Payments.Select(p => (p.Kind, p.Amount.Amount, p.Method)).ShouldBe(
        [
            (ReceiptPaymentKind.Part, 5m, PaymentMethod.CardAtSalonTerminal),
            (ReceiptPaymentKind.Visit, 15m, PaymentMethod.Cash),
            (ReceiptPaymentKind.Products, 12.50m, PaymentMethod.Cash),
        ]);
    }

    /// <summary>A cut with £5 paid by card first, two bottles of shampoo rung up, the rest paid in cash, one bottle brought back.</summary>
    private async Task<Guid> PaidVisitAsync(Guid client, int hoursEarlier = 0)
    {
        var start = Now - Duration.FromMinutes(30) - Duration.FromHours(hoursEarlier);
        var visit = await VisitSeed.AppointmentAsync(factory, _studio.Id, client, _staffId, _serviceId, start, AppointmentStatus.Arrived);
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var account = scope.ServiceProvider.GetRequiredService<IClientAccountService>();
        await account.PayPartAsync(visit, 5m, PaymentMethod.CardAtSalonTerminal, null);
        await account.AddProductAsync(visit, _shampoo, 2, null);
        await account.PayBillAsync(visit, PaymentMethod.Cash, [], null);
        var line = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().VisitProductLines.AsNoTracking().SingleAsync(l => l.AppointmentId == visit);
        await account.ReturnProductAsync(visit, line.Id, 1, PaymentMethod.Cash, true, null);
        return visit;
    }

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken)
        => client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent([new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken))]));
}