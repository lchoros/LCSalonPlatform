using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// What the desk does once a visit is paid (ADR 0039): the checkout offers the next visit in one tap, the booking
/// form opens with the client, the service, the person and the day filled in, and the client's card lists what they
/// bought and has its own "Book a visit".
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class AfterTheVisitTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();

    private Tenant _studio = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        (_staffId, _serviceId) = await VisitSeed.SalonAsync(factory, _studio.Id);
        _clientId = await VisitSeed.ClientAsync(factory, _studio.Id, "Olivia", "Brown", "+447700900123");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_paid_visit_offers_the_next_one_in_four_six_and_eight_weeks_and_the_booking_form_opens_filled_in()
    {
        var start = Now - Duration.FromMinutes(30);
        var visit = await VisitAsync(start, AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var visitDay = start.InZone(London).Date;

        var before = await GetAsync(desk, CheckoutPath(visit));
        before.ShouldNotContain("data-testid=\"rebook-step\"", Case.Sensitive, "the next visit is offered once this one is done");

        (await PostAsync(desk, CheckoutPath(visit), [new("method", "Cash")], before)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var after = await GetAsync(desk, CheckoutPath(visit));

        after.ShouldContain("data-testid=\"rebook-step\"");
        after.ShouldContain("Cut with Anna again.");
        Choices(after).Select(c => c.Weeks).ShouldBe(["4", "6", "8"]);
        after.ShouldContain(">In 4 weeks<");
        var six = Choices(after).Single(c => c.Weeks == "6").Url;
        var expectedDay = LocalDatePattern.Iso.Format(visitDay.PlusWeeks(6));
        six.ShouldBe($"/admin/appointments/new?clientId={_clientId}&serviceId={_serviceId}&staffId={_staffId}&date={expectedDay}");

        var form = await GetAsync(desk, six);
        form.ShouldContain("data-testid=\"selected-client\"");
        form.ShouldContain("Olivia Brown");
        form.ShouldContain($"<option value=\"{_serviceId}\" selected=\"selected\">");
        form.ShouldContain($"<option value=\"{_staffId}\" selected=\"selected\">");
        form.ShouldContain($"type=\"date\" value=\"{expectedDay}\"");
    }

    [Fact]
    public async Task The_salons_own_weeks_are_used_and_a_choice_set_to_nothing_is_left_out()
    {
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Completed);
        await using (var scope = factory.CreateTenantScope(_studio.Id))
        {
            await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new RebookSettings { FirstWeeks = 5, SecondWeeks = 0, ThirdWeeks = 12 });
        }

        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        Choices(await GetAsync(desk, CheckoutPath(visit))).Select(c => c.Weeks).ShouldBe(["5", "12"]);
    }

    [Fact]
    public async Task A_visit_that_is_not_done_or_did_not_take_place_offers_no_next_visit()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        foreach (var status in new[] { AppointmentStatus.Booked, AppointmentStatus.Arrived, AppointmentStatus.NoShow, AppointmentStatus.CancelledByClient })
        {
            var visit = await VisitAsync(Now - Duration.FromHours(2 + (int)status), status);
            (await GetAsync(desk, CheckoutPath(visit))).ShouldNotContain("data-testid=\"rebook-step\"", Case.Sensitive, status.ToString());
        }
    }

    [Fact]
    public async Task The_client_card_lists_what_the_client_bought_and_books_a_visit_in_one_tap()
    {
        var shampoo = await ProductAsync("Shampoo 250 ml", 12.50m);
        var oil = await ProductAsync("Hair oil 100 ml", 18m);
        var visit = await VisitAsync(Now - Duration.FromMinutes(30), AppointmentStatus.Arrived);
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);

        var empty = await GetAsync(desk, $"/admin/clients/{_clientId}");
        empty.ShouldNotContain("data-testid=\"client-purchases\"");
        empty.ShouldContain($"href=\"/admin/appointments/new?clientId={_clientId}\" data-testid=\"book-visit\"");

        await AccountAsync(s => s.AddProductAsync(visit, shampoo, 2, null));
        await AccountAsync(s => s.AddProductAsync(visit, oil, 1, null));
        (await GetAsync(desk, $"/admin/clients/{_clientId}")).ShouldNotContain("data-testid=\"client-purchases\"", Case.Sensitive, "rung up is not bought");

        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        var card = await GetAsync(desk, $"/admin/clients/{_clientId}");

        card.ShouldContain("data-testid=\"client-purchases\"");
        card.ShouldContain("Products bought");
        Regex.Count(card, "data-testid=\"purchase-row\"").ShouldBe(2);
        card.ShouldContain("data-testid=\"purchase-name\">Shampoo 250 ml × 2<");
        card.ShouldContain("data-testid=\"purchase-total\">£25.00<");
        card.ShouldContain("data-testid=\"purchase-name\">Hair oil 100 ml × 1<");
        card.ShouldContain($"href=\"{CheckoutPath(visit)}\" class=\"flex min-h-11 items-center justify-between gap-3 p-3 hover:bg-stone-50\" data-testid=\"purchase-link\"");

        var booking = await GetAsync(desk, $"/admin/appointments/new?clientId={_clientId}");
        booking.ShouldContain("data-testid=\"selected-client\"");
        booking.ShouldContain("Olivia Brown");
    }

    [Fact]
    public async Task Purchases_belong_to_the_client_who_paid_and_only_the_latest_are_shown()
    {
        var shampoo = await ProductAsync("Shampoo 250 ml", 12.50m);
        var other = await VisitSeed.ClientAsync(factory, _studio.Id, "Noah", "Reed", "+447700900555");
        for (var i = 0; i < PurchaseLine.MaxShown + 2; i++)
        {
            var visit = await VisitAsync(Now - Duration.FromDays(i + 1), AppointmentStatus.Completed);
            await AccountAsync(s => s.AddProductAsync(visit, shampoo, 1, null));
            await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.Cash, [], null));
        }

        var purchases = await AccountAsync(s => s.GetPurchasesAsync(_clientId));

        purchases.Count.ShouldBe(PurchaseLine.MaxShown);
        purchases.ShouldAllBe(p => p.ProductName == "Shampoo 250 ml" && p.Quantity == 1 && p.Total.Amount == 12.50m && p.Method == PaymentMethod.Cash);
        purchases.Select(p => p.PaidAt.ToInstant()).ShouldBeInOrder(SortDirection.Descending);
        (await AccountAsync(s => s.GetPurchasesAsync(other))).ShouldBeEmpty();
        (await AccountAsync(s => s.GetPurchasesAsync(Guid.NewGuid()))).ShouldBeEmpty();
    }

    private static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    private static List<(string Weeks, string Url)> Choices(string html)
        => ChoicePattern().Matches(html).Select(m => (m.Groups["weeks"].Value, m.Groups["url"].Value)).ToList();

    private Task<Guid> VisitAsync(Instant start, AppointmentStatus status)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, start, status);

    private async Task<Guid> ProductAsync(string name, decimal price)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, Currency = "GBP" };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, 50, null, null);
        return product.Id;
    }

    private async Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<IClientAccountService>());
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

    [GeneratedRegex("<a href=\"(?<url>[^\"]+)\"[^>]*data-testid=\"rebook-choice\" data-weeks=\"(?<weeks>\\d+)\"")]
    private static partial Regex ChoicePattern();
}