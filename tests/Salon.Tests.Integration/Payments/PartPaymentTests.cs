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

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// Paying one bill two ways (ADR 0061): "£8 in cash and the rest by card". The part is recorded first and the visit
/// has that much less left; the payment that settles it is still one. A part is less than what is left, so it never
/// ends a visit by itself; nobody takes more than the visit costs, even from two tills at once; the money counts on
/// the till under each method. A cut is £20, a shampoo £12.50.
/// </summary>
[Collection(SharedDatabase.Name)]
public class PartPaymentTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";

    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Instant Now = SystemClock.Instance.GetCurrentInstant();
    private static readonly LocalDate Today = Now.InZone(London).Date;

    private Tenant _studio = null!;
    private Tenant _polish = null!;
    private Guid _staffId;
    private Guid _serviceId;
    private Guid _clientId;
    private int _visits;

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
    public async Task A_part_in_cash_and_the_rest_by_card_is_two_lines_and_one_paid_visit()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "desk@studio-20.test", Role.Receptionist);
        var visit = await VisitAsync(AppointmentStatus.Arrived);

        var afterPart = await AccountAsync(s => s.PayPartAsync(visit, 8m, PaymentMethod.Cash, user.Id));

        (afterPart.PartPaid?.Amount, afterPart.Outstanding.Amount, afterPart.Payment, afterPart.HasSomethingToPay).ShouldBe((8m, 12m, null, true));
        var part = afterPart.PartPayments.ShouldHaveSingleItem();
        (part.Amount.Amount, part.Method, part.IsPart, part.ByName).ShouldBe((8m, PaymentMethod.Cash, true, user.DisplayName));

        var paid = await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.CardAtSalonTerminal, [], user.Id));

        (paid.Outstanding.Amount, paid.HasSomethingToPay, paid.PartPaid?.Amount).ShouldBe((0m, false, 8m));
        paid.Payment.ShouldNotBeNull().Amount.Amount.ShouldBe(12m, "the payment that settles the visit is what was left");
        paid.Payment.IsPart.ShouldBeFalse();
        (await EntriesAsync()).Select(e => (e.Kind, e.Amount, e.Method, e.ClientId, e.AppointmentId, e.ByUserId)).ShouldBe(
        [
            (ClientAccountEntryKind.PartPayment, 8m, PaymentMethod.Cash, _clientId, visit, user.Id),
            (ClientAccountEntryKind.VisitPayment, 12m, PaymentMethod.CardAtSalonTerminal, _clientId, visit, user.Id),
        ]);

        var till = await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));
        till.Totals.Select(t => (t.Method, t.Amount.Amount)).ShouldBe([(PaymentMethod.Cash, 8m), (PaymentMethod.CardAtSalonTerminal, 12m)]);
        (till.Takings.Amount, till.ExpectedCash.Amount).ShouldBe((20m, 8m), "the cash part is in the drawer");
        var report = await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(null, Today, Today));
        report.Takings.Total.Amount.ShouldBe(20m, "the visit is counted once, whatever the number of lines");
        report.Takings.ByKind.Select(k => (k.Kind, k.Amount.Amount)).ShouldBe([(ClientAccountEntryKind.VisitPayment, 12m), (ClientAccountEntryKind.PartPayment, 8m)]);
    }

    [Fact]
    public async Task Three_ways_and_an_own_method_work_and_a_part_comes_off_after_a_deposit_and_a_voucher()
    {
        var payPal = (await ScopedAsync<IPaymentMethodService, PaymentMethodResult>(s => s.AddAsync("PayPal"))).Id!.Value;
        var visit = await VisitAsync(AppointmentStatus.Booked);
        await AccountAsync(s => s.RecordDepositAsync(visit, 5m, PaymentChoice.Own(payPal), null));
        await SetStatusAsync(visit, AppointmentStatus.Arrived);
        var voucher = await ScopedAsync<IGiftVoucherService, VoucherSummary>(s => s.SellAsync(4m, PaymentMethod.Cash, null, null, null));
        await ScopedAsync<IGiftVoucherService, VoucherApplied>(s => s.ApplyAsync(visit, voucher.Code, null));

        // £20 less the £5 deposit and the £4 voucher: £11 is left. £3 by PayPal, £2 in cash, the rest by card.
        (await AccountAsync(s => s.PayPartAsync(visit, 3m, PaymentChoice.Own(payPal), null))).Outstanding.Amount.ShouldBe(8m);
        var bill = await AccountAsync(s => s.PayPartAsync(visit, 2m, PaymentMethod.Cash, null));
        (bill.Outstanding.Amount, bill.PartPaid?.Amount).ShouldBe((6m, 5m));
        bill.PartPayments.Select(p => (p.Amount.Amount, p.Method, p.MethodName)).ShouldBe([(3m, PaymentMethod.Other, "PayPal"), (2m, PaymentMethod.Cash, null)]);

        var paid = await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.CardAtSalonTerminal, [], null));
        paid.Payment.ShouldNotBeNull().Amount.Amount.ShouldBe(6m);

        // The visit's own money is its price less the voucher, whose money came in when it was sold.
        var forVisit = (await EntriesAsync()).Where(e => e.AppointmentId == visit && e.Method is not null).Sum(e => e.Amount);
        forVisit.ShouldBe(16m);
    }

    [Fact]
    public async Task A_part_is_more_than_nothing_and_less_than_what_is_left()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);

        foreach (var amount in new[] { 0m, -5m, 20m, 20.01m, 500m, 7.555m })
        {
            (await RefusalAsync(s => s.PayPartAsync(visit, amount, PaymentMethod.Cash, null))).ShouldBe(ClientAccountRefusal.PartNotLessThanDue, amount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        await AccountAsync(s => s.PayPartAsync(visit, 19.99m, PaymentMethod.Cash, null));
        (await RefusalAsync(s => s.PayPartAsync(visit, 0.01m, PaymentMethod.Cash, null))).ShouldBe(ClientAccountRefusal.PartNotLessThanDue, "the last penny is the payment, not a part");
        (await EntriesAsync()).ShouldHaveSingleItem().Amount.ShouldBe(19.99m);

        // A method the salon does not have, a visit nobody turned up for, one already paid, one that does not exist.
        (await RefusalAsync(s => s.PayPartAsync(visit, 0.005m, PaymentChoice.Own(Guid.NewGuid()), null))).ShouldBe(ClientAccountRefusal.MethodNotOffered);
        var booked = await VisitAsync(AppointmentStatus.Booked, Duration.FromDays(2));
        (await RefusalAsync(s => s.PayPartAsync(booked, 5m, PaymentMethod.Cash, null))).ShouldBe(ClientAccountRefusal.VisitNotPayable);
        var missed = await VisitAsync(AppointmentStatus.NoShow);
        (await RefusalAsync(s => s.PayPartAsync(missed, 5m, PaymentMethod.Cash, null))).ShouldBe(ClientAccountRefusal.VisitNotPayable);
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.CardAtSalonTerminal, [], null));
        (await RefusalAsync(s => s.PayPartAsync(visit, 0.01m, PaymentMethod.Cash, null))).ShouldBe(ClientAccountRefusal.PartNotLessThanDue, "nothing is left of a paid visit");
        (await RefusalAsync(s => s.PayPartAsync(Guid.NewGuid(), 5m, PaymentMethod.Cash, null))).ShouldBe(ClientAccountRefusal.NotFound);
        (await RefusalAsync(s => s.PayPartAsync(visit, 5m, PaymentMethod.Cash, null), _polish.Id)).ShouldBe(ClientAccountRefusal.NotFound, "another salon has no such visit");
        (await EntriesAsync()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task The_same_form_sent_twice_is_one_part()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        var formId = Guid.CreateVersion7();

        var first = await AccountAsync(s => s.PayPartAsync(visit, 8m, PaymentMethod.Cash, null, formId));
        var second = await AccountAsync(s => s.PayPartAsync(visit, 8m, PaymentMethod.Cash, null, formId));

        (first.Outstanding.Amount, second.Outstanding.Amount).ShouldBe((12m, 12m));
        var line = (await EntriesAsync()).ShouldHaveSingleItem();
        (line.Id, line.Amount).ShouldBe((formId, 8m));

        // The id of some other line of money is not a form of ours.
        var other = await VisitAsync(AppointmentStatus.Arrived);
        (await RefusalAsync(s => s.PayPartAsync(other, 5m, PaymentMethod.Cash, null, formId))).ShouldBe(ClientAccountRefusal.AlreadyBeingRecorded);
        (await EntriesAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Two_tills_cannot_take_more_than_the_visit_costs()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);

        // Six requests at once, each for £15 of a £20 visit: one fits.
        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try
            {
                await AccountAsync(s => s.PayPartAsync(visit, 15m, PaymentMethod.Cash, null));
                return true;
            }
            catch (ClientAccountException ex) when (ex.Refusal == ClientAccountRefusal.PartNotLessThanDue)
            {
                return false;
            }
        }));

        attempts.Count(won => won).ShouldBe(1);
        (await EntriesAsync()).ShouldHaveSingleItem().Amount.ShouldBe(15m);
        (await AccountAsync(s => s.GetBillAsync(visit))).ShouldNotBeNull().Outstanding.Amount.ShouldBe(5m);
    }

    [Fact]
    public async Task A_completed_visit_with_a_part_paid_owes_what_is_left_and_products_are_paid_with_the_rest()
    {
        var shampoo = await ProductAsync("Shampoo 250 ml", 12.50m);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(visit, shampoo, 1, null));
        await AccountAsync(s => s.PayPartAsync(visit, 8m, PaymentMethod.Cash, null));
        await SetStatusAsync(visit, AppointmentStatus.Completed);

        var report = await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(null, Today, Today));
        (report.UnpaidCount, report.UnpaidTotal.Amount).ShouldBe((1, 12m));

        // The part is toward the visit; the products are paid with the rest, by the second method.
        var bill = (await AccountAsync(s => s.GetBillAsync(visit))).ShouldNotBeNull();
        (bill.Outstanding.Amount, bill.ProductsOutstanding.Amount, bill.ToPay.Amount).ShouldBe((12m, 12.50m, 24.50m));
        await AccountAsync(s => s.PayBillAsync(visit, PaymentMethod.CardAtSalonTerminal, [], null));
        (await EntriesAsync()).Select(e => (e.Kind, e.Amount, e.Method)).ShouldBe(
        [
            (ClientAccountEntryKind.PartPayment, 8m, PaymentMethod.Cash),
            (ClientAccountEntryKind.VisitPayment, 12m, PaymentMethod.CardAtSalonTerminal),
            (ClientAccountEntryKind.ProductSale, 12.50m, PaymentMethod.CardAtSalonTerminal),
        ]);
        (await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(null, Today, Today))).UnpaidCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_desk_records_a_part_at_the_checkout_and_pays_the_rest()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        var bill = await GetAsync(desk, CheckoutPath(visit));
        bill.ShouldContain("data-testid=\"part-step\"");
        bill.ShouldContain("Less than the £20.00 left for the visit.");

        var recorded = await PostAsync(desk, CheckoutPath(visit) + "/part", bill, ("amount", "8"), ("method", "Cash"), ("part", Guid.CreateVersion7().ToString()));

        recorded.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        recorded.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe(CheckoutPath(visit));
        bill = await GetAsync(desk, CheckoutPath(visit));
        bill.ShouldContain("£8.00 is recorded. £12.00 is left to pay for the visit.");
        bill.ShouldContain("Part paid: Cash");
        bill.ShouldContain("Less than the £12.00 left for the visit.");

        // The whole of what is left is not a part; letters are not an amount; no method is no part.
        (await PostAsync(desk, CheckoutPath(visit) + "/part", bill, ("amount", "12"), ("method", "Cash"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(desk, CheckoutPath(visit))).ShouldContain("A part is more than nothing and less than what is left to pay for the visit.");
        (await PostAsync(desk, CheckoutPath(visit) + "/part", bill, ("amount", "a few"), ("method", "Cash"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(desk, CheckoutPath(visit))).ShouldContain("Enter the amount of the part and choose how it was paid.");
        (await PostAsync(desk, CheckoutPath(visit) + "/part", bill, ("amount", "5"))).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await GetAsync(desk, CheckoutPath(visit))).ShouldContain("Enter the amount of the part and choose how it was paid.");
        (await EntriesAsync()).ShouldHaveSingleItem();

        var paid = await PostAsync(desk, CheckoutPath(visit), bill, ("method", "CardAtSalonTerminal"));
        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect, await paid.Content.ReadAsStringAsync());
        var settled = await GetAsync(desk, CheckoutPath(visit));
        settled.ShouldContain("Part paid: Cash");
        settled.ShouldContain("Paid: Card at the salon's terminal");
        settled.ShouldNotContain("data-testid=\"part-step\"");
        (await StatusAsync(visit)).ShouldBe(AppointmentStatus.Completed);
        (await GetAsync(desk, "/admin/payments/cash-up")).ShouldContain("Visit paid in part");
    }

    [Fact]
    public async Task Only_the_logins_that_take_money_record_a_part_and_another_salon_has_no_such_visit()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var bill = await GetAsync(owner, CheckoutPath(visit));

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        var denied = await PostAsync(staff, CheckoutPath(visit) + "/part", bill, ("amount", "5"), ("method", "Cash"));
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var noToken = await owner.PostAsync(new Uri(CheckoutPath(visit) + "/part", UriKind.Relative), new FormUrlEncodedContent([new("amount", "5"), new("method", "Cash")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var theirOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        var theirPage = await GetAsync(theirOwner, "/admin/payments/cash-up");
        (await PostAsync(theirOwner, CheckoutPath(visit) + "/part", theirPage, ("amount", "5"), ("method", "Cash"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EntriesAsync()).ShouldBeEmpty();
    }

    private static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    /// <summary>A visit today, each a little earlier than the last so no two overlap; or so far from now.</summary>
    private Task<Guid> VisitAsync(AppointmentStatus status, Duration? fromNow = null)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now + (fromNow ?? Duration.Zero) - Duration.FromMinutes(35 * ++_visits), status);

    private async Task SetStatusAsync(Guid appointmentId, AppointmentStatus status)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, status));
    }

    private async Task<AppointmentStatus> StatusAsync(Guid appointmentId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Appointments.AsNoTracking().Where(a => a.Id == appointmentId).Select(a => a.Status).SingleAsync();
    }

    private async Task<Guid> ProductAsync(string name, decimal price)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var product = new Product { Name = name, RetailPrice = price, Currency = "GBP" };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        (await scope.ServiceProvider.GetRequiredService<IStockService>().RecordAsync(product.Id, StockMovementKind.Counted, 20, null, null)).Outcome.ShouldBe(StockOutcome.Recorded);
        return product.Id;
    }

    private Task<T> AccountAsync<T>(Func<IClientAccountService, Task<T>> act) => ScopedAsync(act);

    private async Task<ClientAccountRefusal> RefusalAsync<T>(Func<IClientAccountService, Task<T>> act, Guid? tenantId = null)
        => (await Should.ThrowAsync<ClientAccountException>(() => ScopedAsync(act, tenantId))).Refusal;

    private async Task<T> ScopedAsync<TService, T>(Func<TService, Task<T>> act, Guid? tenantId = null)
        where TService : notnull
    {
        await using var scope = factory.CreateTenantScope(tenantId ?? _studio.Id);
        return await act(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private async Task<List<ClientAccountEntry>> EntriesAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries.AsNoTracking().OrderBy(e => e.At).ThenBy(e => e.Id).ToListAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string pageWithToken, params (string Key, string Value)[] fields)
        => client.PostAsync(new Uri(path, UriKind.Relative), new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("__RequestVerificationToken", IdentityTestClient.AntiForgeryToken(pageWithToken)), .. fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))]));

    private static async Task<string> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, path);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }
}