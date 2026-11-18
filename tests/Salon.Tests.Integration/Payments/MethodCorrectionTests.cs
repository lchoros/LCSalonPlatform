using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Reports;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Products;
using Salon.Services.Reports;
using Salon.Services.Settings;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Payments;

/// <summary>
/// Putting right money recorded under the wrong method (ADR 0064): "I pressed cash, it was card". The line is never
/// edited: two lines are added that take its amount off one method and put it on the other. The day's takings stay
/// what they were, each method's total becomes right, and every page that says how something was paid says the
/// corrected method. Only a line of today can be corrected, and one line is never corrected twice at once. A cut is
/// £20, a shampoo £12.50.
/// </summary>
[Collection(SharedDatabase.Name)]
public class MethodCorrectionTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string HostA = "studio-20.localhost";
    private const string HostB = "salon-pl.localhost";
    private const PaymentMethod Cash = PaymentMethod.Cash;
    private const PaymentMethod Card = PaymentMethod.CardAtSalonTerminal;
    private const ClientAccountEntryKind PutOn = ClientAccountEntryKind.MethodCorrectionIn;
    private const ClientAccountEntryKind TakenOff = ClientAccountEntryKind.MethodCorrectionOut;

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

        // A missed visit costs half its price, so there is a fee to pay and to correct.
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<ISettingService>().SaveAsync(new CancellationPolicySettings { NoShowFeePercent = 50m });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_visit_recorded_as_cash_and_corrected_to_card_moves_the_money_and_keeps_the_takings()
    {
        var user = await factory.CreateUserAsync(_studio.Id, "desk@studio-20.test", Role.Receptionist);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], user.Id));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();
        var before = await TillAsync();
        (before.Takings.Amount, before.ExpectedCash.Amount).ShouldBe((20m, 20m));
        before.Movements.ShouldHaveSingleItem().CanCorrect.ShouldBeTrue();

        var result = await CorrectAsync(payment.Id, Card, user.Id);

        (result.Outcome, result.Day, result.Now?.Method).ShouldBe((MethodCorrectionOutcome.Corrected, Today, Card));
        var entries = await EntriesAsync();
        entries.Select(e => (e.Kind, e.Amount, e.Method, e.ClientId, e.AppointmentId, e.RelatedEntryId, e.ByUserId)).ShouldBe(
        [
            (ClientAccountEntryKind.VisitPayment, 20m, Cash, _clientId, visit, null, user.Id),
            (TakenOff, 20m, Cash, _clientId, visit, payment.Id, user.Id),
            (PutOn, 20m, Card, _clientId, visit, payment.Id, user.Id),
        ]);
        entries[0].ShouldBeEquivalentTo(payment, "the line itself is never edited");

        var till = await TillAsync();
        till.Totals.Select(t => (t.Method, t.Amount.Amount)).ShouldBe([(Cash, 0m), (Card, 20m)]);
        (till.Takings.Amount, till.ExpectedCash.Amount).ShouldBe((20m, 0m), "the same takings, and no cash in the drawer from this visit");
        till.Movements.Select(m => (m.Kind, m.Method, m.Amount.Amount, m.CanCorrect, m.CorrectedTo?.Method)).ShouldBe(
        [
            (ClientAccountEntryKind.VisitPayment, Cash, 20m, true, Card),
            (TakenOff, Cash, -20m, false, null),
            (PutOn, Card, 20m, false, null),
        ]);

        // The bill, the report and the client's own copy read as it was paid.
        (await AccountAsync(s => s.GetBillAsync(visit))).ShouldNotBeNull().Payment.ShouldNotBeNull().Method.ShouldBe(Card);
        var report = await ScopedAsync<IReportService, SalonReport>(s => s.GetAsync(null, Today, Today));
        report.Takings.Total.Amount.ShouldBe(20m);
        report.Takings.ByMethod.Select(t => (t.Method, t.Amount.Amount)).ShouldBe([(Cash, 0m), (Card, 20m)]);
        report.Takings.ByKind.Select(k => (k.Kind, k.Amount.Amount)).ShouldBe([(ClientAccountEntryKind.VisitPayment, 20m)], "a correction is not a kind of takings");
    }

    [Fact]
    public async Task Money_that_went_out_is_corrected_the_other_way_round()
    {
        var visit = await VisitAsync(AppointmentStatus.Booked, Duration.FromDays(2));
        await AccountAsync(s => s.RecordDepositAsync(visit, 10m, Cash, null));
        await AccountAsync(s => s.ReturnDepositAsync(visit, Cash, null));
        var returned = (await EntriesAsync()).Single(e => e.Kind == ClientAccountEntryKind.DepositReturned);
        (await TillAsync()).ExpectedCash.Amount.ShouldBe(0m);

        // The deposit went back by bank transfer, not out of the drawer.
        (await CorrectAsync(returned.Id, PaymentMethod.BankTransfer)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected);

        (await EntriesAsync()).Skip(2).Select(e => (e.Kind, e.Amount, e.Method, e.RelatedEntryId)).ShouldBe(
        [
            (PutOn, 10m, Cash, returned.Id),
            (TakenOff, 10m, PaymentMethod.BankTransfer, returned.Id),
        ]);
        var till = await TillAsync();
        till.Totals.Select(t => (t.Method, t.Amount.Amount)).ShouldBe([(Cash, 10m), (PaymentMethod.BankTransfer, -10m)]);
        (till.Takings.Amount, till.ExpectedCash.Amount).ShouldBe((0m, 10m), "the deposit is still in the drawer");
        var deposit = (await AccountAsync(s => s.GetDepositAsync(visit))).ShouldNotBeNull().Deposit.ShouldNotBeNull();
        (deposit.Method, deposit.Return.ShouldNotBeNull().Method).ShouldBe((Cash, PaymentMethod.BankTransfer));
    }

    [Fact]
    public async Task A_line_is_corrected_again_and_to_and_from_one_of_the_salons_own_methods()
    {
        var payPal = (await ScopedAsync<IPaymentMethodService, PaymentMethodResult>(s => s.AddAsync("PayPal"))).Id!.Value;
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();

        var first = await CorrectAsync(payment.Id, PaymentChoice.Own(payPal));
        (first.Outcome, first.Now?.Method, first.Now?.MethodName).ShouldBe((MethodCorrectionOutcome.Corrected, PaymentMethod.Other, "PayPal"));
        var bill = (await AccountAsync(s => s.GetBillAsync(visit))).ShouldNotBeNull();
        (bill.Payment.ShouldNotBeNull().Method, bill.Payment.MethodName).ShouldBe((PaymentMethod.Other, "PayPal"));

        // The salon renames the method, then finds it was card after all: the line that undoes carries today's name.
        await ScopedAsync<IPaymentMethodService, PaymentMethodResult>(s => s.RenameAsync(payPal, "PayPal app"));
        (await CorrectAsync(payment.Id, Card)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected);

        (await EntriesAsync()).Select(e => (e.Kind, e.Method, e.CustomMethodId, e.MethodName)).ShouldBe(
        [
            (ClientAccountEntryKind.VisitPayment, Cash, null, null),
            (TakenOff, Cash, null, null),
            (PutOn, PaymentMethod.Other, payPal, "PayPal"),
            (TakenOff, PaymentMethod.Other, payPal, "PayPal app"),
            (PutOn, Card, null, null),
        ]);
        var till = await TillAsync();
        till.Totals.Select(t => (t.Method, t.MethodName, t.Amount.Amount)).ShouldBe([(Cash, null, 0m), (Card, null, 20m), (PaymentMethod.Other, "PayPal app", 0m)]);
        till.Movements[0].CorrectedTo.ShouldNotBeNull().Method.ShouldBe(Card, "the latest correction is where the line stands");
        (await AccountAsync(s => s.GetBillAsync(visit))).ShouldNotBeNull().Payment.ShouldNotBeNull().Method.ShouldBe(Card);
        var line = (await ScopedAsync<IMethodCorrectionService, CorrectableLine?>(s => s.GetAsync(payment.Id))).ShouldNotBeNull();
        (line.Recorded.Method, line.StandsAt.Method, line.WasCorrected, line.IsToday, line.Amount.Amount, line.ClientName).ShouldBe((Cash, Card, true, true, 20m, "Olivia Brown"));

        // And back to what was recorded at first: the line reads cash again and the till is as it began.
        (await CorrectAsync(payment.Id, Cash)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected);
        var back = await TillAsync();
        (back.ExpectedCash.Amount, back.Takings.Amount).ShouldBe((20m, 20m));
        back.Totals.Single(t => t.Method == Cash).Amount.Amount.ShouldBe(20m);
    }

    [Fact]
    public async Task Every_page_that_says_how_something_was_paid_says_the_corrected_method()
    {
        var shampoo = await ProductAsync("Shampoo 250 ml", 12.50m);

        // A deposit, a fee, products on a bill, a sale at the counter and a gift voucher: all recorded as cash.
        var booked = await VisitAsync(AppointmentStatus.Booked, Duration.FromDays(2));
        await AccountAsync(s => s.RecordDepositAsync(booked, 5m, Cash, null));
        var missed = await VisitAsync(AppointmentStatus.NoShow);
        await AccountAsync(s => s.ChargeFeeAsync(missed, 10m, null));
        await AccountAsync(s => s.RecordFeePaymentAsync(missed, Cash, null));
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.AddProductAsync(visit, shampoo, 1, null));
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var counter = await ScopedAsync<ICounterSaleService, CounterSaleReceipt>(s => s.SellAsync([(shampoo, 2)], Cash, null));
        var voucher = await ScopedAsync<IGiftVoucherService, VoucherSummary>(s => s.SellAsync(30m, Cash, null, null, null));
        var cashLines = (await EntriesAsync()).Where(e => e.Method == Cash).ToList();
        cashLines.Select(e => e.Kind).ShouldBe(
        [
            ClientAccountEntryKind.DepositPaid, ClientAccountEntryKind.FeePayment, ClientAccountEntryKind.VisitPayment,
            ClientAccountEntryKind.ProductSale, ClientAccountEntryKind.ProductSale, ClientAccountEntryKind.VoucherSale,
        ]);
        var takings = (await TillAsync()).Takings.Amount;

        foreach (var line in cashLines)
        {
            (await CorrectAsync(line.Id, Card)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected, line.Kind.ToString());
        }

        (await AccountAsync(s => s.GetDepositAsync(booked))).ShouldNotBeNull().Deposit.ShouldNotBeNull().Method.ShouldBe(Card);
        (await AccountAsync(s => s.GetFeeAsync(missed))).ShouldNotBeNull().Fee.ShouldNotBeNull().Settlement.ShouldNotBeNull().Method.ShouldBe(Card);
        var bill = (await AccountAsync(s => s.GetBillAsync(visit))).ShouldNotBeNull();
        (bill.Payment.ShouldNotBeNull().Method, bill.Products.ShouldHaveSingleItem().PaidWith).ShouldBe((Card, Card));
        (await AccountAsync(s => s.GetPurchasesAsync(_clientId))).ShouldHaveSingleItem().Method.ShouldBe(Card);
        (await ScopedAsync<ICounterSaleService, CounterSaleDay>(s => s.ListAsync(null))).Sales.ShouldHaveSingleItem().Method.ShouldBe(Card);
        (await ScopedAsync<IGiftVoucherService, VoucherCard?>(s => s.GetAsync(voucher.Id))).ShouldNotBeNull().SoldWith.ShouldBe(Card);

        // A sale at the counter and a voucher belong to no client: neither do the lines that correct them.
        var entries = await EntriesAsync();
        entries.Where(e => e.RelatedEntryId == counter.EntryId || e.RelatedEntryId == cashLines[^1].Id).Where(e => ClientAccountRules.IsMethodCorrection(e.Kind))
            .Select(e => (e.ClientId, e.AppointmentId)).Distinct().ShouldBe([(null, null)]);
        var till = await TillAsync();
        till.Totals.Select(t => (t.Method, t.Amount.Amount)).ShouldBe([(Cash, 0m), (Card, takings)]);
        (till.Takings.Amount, till.ExpectedCash.Amount).ShouldBe((takings, 0m));
    }

    [Fact]
    public async Task Only_a_line_of_today_that_moved_money_is_corrected_and_only_to_another_method_the_salon_has()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();
        var missed = await VisitAsync(AppointmentStatus.NoShow);
        await AccountAsync(s => s.ChargeFeeAsync(missed, 10m, null));
        var voucher = await ScopedAsync<IGiftVoucherService, VoucherSummary>(s => s.SellAsync(30m, Cash, null, null, null));
        var entries = await EntriesAsync();
        var fee = entries.Single(e => e.Kind == ClientAccountEntryKind.NoShowFee);
        var voucherSale = entries.Single(e => e.Kind == ClientAccountEntryKind.VoucherSale);

        (await CorrectAsync(payment.Id, Cash)).Outcome.ShouldBe(MethodCorrectionOutcome.SameMethod);
        (await CorrectAsync(payment.Id, PaymentChoice.Own(Guid.NewGuid()))).Outcome.ShouldBe(MethodCorrectionOutcome.MethodNotOffered);
        (await CorrectAsync(payment.Id, PaymentMethod.Other)).Outcome.ShouldBe(MethodCorrectionOutcome.MethodNotOffered, "\"other\" without saying which");
        (await CorrectAsync(voucherSale.Id, PaymentMethod.Voucher)).Outcome.ShouldBe(MethodCorrectionOutcome.MethodNotOffered, "a voucher is not bought with a voucher");
        (await CorrectAsync(fee.Id, Card)).Outcome.ShouldBe(MethodCorrectionOutcome.NotCorrectable, "a fee added moved no money");
        (await CorrectAsync(Guid.NewGuid(), Card)).Outcome.ShouldBe(MethodCorrectionOutcome.NotFound);
        (await CorrectAsync(payment.Id, Card, tenantId: _polish.Id)).Outcome.ShouldBe(MethodCorrectionOutcome.NotFound, "another salon has no such line");
        (await ScopedAsync<IMethodCorrectionService, CorrectableLine?>(s => s.GetAsync(payment.Id), _polish.Id)).ShouldBeNull();
        (await ScopedAsync<IMethodCorrectionService, CorrectableLine?>(s => s.GetAsync(fee.Id))).ShouldBeNull();

        // One of the salon's own methods taken out of use is not a method to correct to.
        var cheque = (await ScopedAsync<IPaymentMethodService, PaymentMethodResult>(s => s.AddAsync("Cheque"))).Id!.Value;
        await ScopedAsync<IPaymentMethodService, PaymentMethodResult>(s => s.RetireAsync(cheque));
        (await CorrectAsync(payment.Id, PaymentChoice.Own(cheque))).Outcome.ShouldBe(MethodCorrectionOutcome.MethodNotOffered);
        (await EntriesAsync()).Count.ShouldBe(entries.Count, "nothing was written by any of these");

        // A correction is not corrected: the line is corrected again.
        (await CorrectAsync(payment.Id, Card)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected);
        var half = (await EntriesAsync()).First(e => e.Kind == PutOn);
        (await CorrectAsync(half.Id, Cash)).Outcome.ShouldBe(MethodCorrectionOutcome.NotCorrectable);
        (await ScopedAsync<IMethodCorrectionService, CorrectableLine?>(s => s.GetAsync(half.Id))).ShouldBeNull();

        // Yesterday's line: a correction made now would change today's till, which did not take that money.
        await MoveToYesterdayAsync(voucherSale.Id);
        var late = await CorrectAsync(voucherSale.Id, Card);
        (late.Outcome, late.Day).ShouldBe((MethodCorrectionOutcome.NotToday, Today.PlusDays(-1)));
        (await ScopedAsync<IMethodCorrectionService, CorrectableLine?>(s => s.GetAsync(voucherSale.Id))).ShouldNotBeNull().IsToday.ShouldBeFalse();
        (await ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(Today.PlusDays(-1)))).Movements.ShouldHaveSingleItem().CanCorrect.ShouldBeFalse();
        (await EntriesAsync()).Count.ShouldBe(entries.Count + 2);
    }

    [Fact]
    public async Task The_same_form_sent_twice_is_one_correction()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();
        var formId = Guid.CreateVersion7();

        var first = await CorrectAsync(payment.Id, Card, correctionId: formId);
        var second = await CorrectAsync(payment.Id, Card, correctionId: formId);

        (first.Outcome, second.Outcome, second.Now?.Method).ShouldBe((MethodCorrectionOutcome.Corrected, MethodCorrectionOutcome.Corrected, Card));
        var entries = await EntriesAsync();
        entries.Count.ShouldBe(3);
        entries.Single(e => e.Kind == TakenOff).Id.ShouldBe(formId);

        // The id of some other line of money is not a form of ours.
        var other = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(other, Cash, [], null));
        var otherPayment = (await EntriesAsync()).Single(e => e.AppointmentId == other);
        (await CorrectAsync(otherPayment.Id, Card, correctionId: formId)).Outcome.ShouldBe(MethodCorrectionOutcome.AlreadyBeingRecorded);
        (await CorrectAsync(otherPayment.Id, Card, correctionId: payment.Id)).Outcome.ShouldBe(MethodCorrectionOutcome.AlreadyBeingRecorded);
        (await EntriesAsync()).Count.ShouldBe(4);
    }

    [Fact]
    public async Task Two_tills_correcting_one_line_at_once_move_the_money_once()
    {
        // One payment is corrected first, so nothing below waits for a first run of anything.
        (await CorrectAsync(await PaidInCashAsync(), Card)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected);
        var payments = new List<Guid>();
        for (var i = 0; i < 8; i++)
        {
            payments.Add(await PaidInCashAsync());
        }

        // On each of eight payments, three requests move the same £20 from cash to card. Every request has its
        // connection open and waits; then all are let go at the same moment.
        var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new List<Task>();
        var attempts = payments.SelectMany(payment => Enumerable.Range(0, 3).Select(_ =>
        {
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.Add(waiting.Task);
            return Task.Run(async () =>
            {
                await using var scope = factory.CreateTenantScope(_studio.Id);
                var service = scope.ServiceProvider.GetRequiredService<IMethodCorrectionService>();
                (await service.GetAsync(payment)).ShouldNotBeNull();
                waiting.SetResult();
                await go.Task;
                return (Payment: payment, (await service.CorrectAsync(payment, Card, null)).Outcome);
            });
        })).ToArray();
        await Task.WhenAll(ready);
        go.SetResult();
        var outcomes = (await Task.WhenAll(attempts)).ToLookup(a => a.Payment, a => a.Outcome);

        // One request moves the money; the others find it done.
        foreach (var payment in payments)
        {
            outcomes[payment].Order().ToArray().ShouldBe([MethodCorrectionOutcome.Corrected, MethodCorrectionOutcome.SameMethod, MethodCorrectionOutcome.SameMethod], string.Join(", ", outcomes[payment]));
        }

        (await EntriesAsync()).Count.ShouldBe(27, "nine payments, each with one line taken off and one put on");
        var till = await TillAsync();
        till.Totals.Select(t => (t.Method, t.Amount.Amount)).ShouldBe([(Cash, 0m), (Card, 180m)]);
        (till.Takings.Amount, till.ExpectedCash.Amount).ShouldBe((180m, 0m));
    }

    /// <summary>A visit paid in cash; the id of the line of money.</summary>
    private async Task<Guid> PaidInCashAsync()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        return (await EntriesAsync()).Single(e => e.AppointmentId == visit).Id;
    }

    [Fact]
    public async Task A_day_closed_before_the_correction_says_what_the_drawer_should_hold_now()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();

        // The count comes up £20 short: that is how the wrong method is found.
        (await ScopedAsync<ICashUpService, CashUpResult>(s => s.CloseAsync(Today, 0m, "£20 short", null))).Outcome.ShouldBe(CashUpOutcome.Closed);
        (await CorrectAsync(payment.Id, Card)).Outcome.ShouldBe(MethodCorrectionOutcome.Corrected);

        var till = await TillAsync();
        var closed = till.Closed.ShouldNotBeNull();
        (closed.ExpectedCash.Amount, closed.CountedCash.Amount, closed.Difference.Amount).ShouldBe((20m, 0m, -20m), "the count stands as it was made");
        (till.ExpectedCash.Amount, till.ChangedSinceClose).ShouldBe((0m, true));
    }

    [Fact]
    public async Task The_desk_opens_a_line_from_the_till_corrects_it_and_reads_it_on_the_till_and_the_bill()
    {
        using var desk = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Receptionist);
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();

        var till = await GetAsync(desk, "/admin/payments/cash-up");
        till.ShouldContain($"href=\"{LinePath(payment.Id)}\"");
        till.ShouldContain("Wrong method?");
        var page = await GetAsync(desk, LinePath(payment.Id));
        page.ShouldContain("Correct how it was paid");
        page.ShouldContain("Olivia Brown");
        page.ShouldContain("Visit paid · £20.00");
        page.ShouldContain("<option value=\"Cash\" selected=\"selected\">Cash</option>");
        page.ShouldNotContain("data-testid=\"correct-stands-at\"");

        // No method, and the method it already stands at: the page again with the reason, nothing written.
        var incomplete = await PostAsync(desk, LinePath(payment.Id), page);
        incomplete.StatusCode.ShouldBe(HttpStatusCode.OK);
        WebUtility.HtmlDecode(await incomplete.Content.ReadAsStringAsync()).ShouldContain("Choose how it was paid.");
        var same = await PostAsync(desk, LinePath(payment.Id), page, ("method", "Cash"));
        WebUtility.HtmlDecode(await same.Content.ReadAsStringAsync()).ShouldContain("It is already recorded that way. Nothing was changed.");
        var unknown = await PostAsync(desk, LinePath(payment.Id), page, ("method", Guid.NewGuid().ToString()));
        WebUtility.HtmlDecode(await unknown.Content.ReadAsStringAsync()).ShouldContain("The salon does not take money that way. Choose another way of paying.");
        (await EntriesAsync()).ShouldHaveSingleItem();

        var corrected = await PostAsync(desk, LinePath(payment.Id), page, ("method", "CardAtSalonTerminal"), ("correction", Guid.CreateVersion7().ToString()));

        corrected.StatusCode.ShouldBe(HttpStatusCode.Redirect, await corrected.Content.ReadAsStringAsync());
        corrected.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/admin/payments/cash-up?date={Today:yyyy-MM-dd}");
        till = await GetAsync(desk, corrected.Headers.Location.OriginalString);
        till.ShouldContain("Corrected. It was paid by: Card at the salon's terminal.");
        till.ShouldContain("corrected to Card at the salon's terminal");
        till.ShouldContain("Method corrected: taken off · Cash");
        till.ShouldContain("Method corrected: put on · Card at the salon's terminal");
        (await GetAsync(desk, $"/admin/payments/checkout/{visit}")).ShouldContain("Paid: Card at the salon's terminal");
        page = await GetAsync(desk, LinePath(payment.Id));
        page.ShouldContain("data-testid=\"correct-stands-at\"");
        page.ShouldContain("<option value=\"CardAtSalonTerminal\" selected=\"selected\">");

        // A line of another day shows why it cannot be corrected and has no form; a post is refused all the same.
        await MoveToYesterdayAsync(payment.Id);
        var old = await GetAsync(desk, LinePath(payment.Id));
        old.ShouldContain("Only a payment recorded today can be corrected here.");
        old.ShouldNotContain("data-testid=\"correct-form\"");
        var late = await PostAsync(desk, LinePath(payment.Id), page, ("method", "Cash"));
        late.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(desk, $"/admin/payments/cash-up?date={Today.PlusDays(-1):yyyy-MM-dd}")).ShouldNotContain("Wrong method?");
        (await EntriesAsync()).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Only_the_logins_that_take_money_correct_a_line_and_another_salon_has_no_such_line()
    {
        var visit = await VisitAsync(AppointmentStatus.Arrived);
        await AccountAsync(s => s.PayBillAsync(visit, Cash, [], null));
        var payment = (await EntriesAsync()).ShouldHaveSingleItem();
        using var owner = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Owner);
        var page = await GetAsync(owner, LinePath(payment.Id));

        using var staff = await factory.SignedInClientAsync(HostA, _studio.Id, Role.Staff);
        using var seen = await staff.GetAsync(new Uri(LinePath(payment.Id), UriKind.Relative));
        seen.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");
        var denied = await PostAsync(staff, LinePath(payment.Id), page, ("method", "CardAtSalonTerminal"));
        denied.Headers.Location.ShouldNotBeNull().AbsolutePath.ShouldBe("/account/denied");

        var noToken = await owner.PostAsync(new Uri(LinePath(payment.Id), UriKind.Relative), new FormUrlEncodedContent([new("method", "CardAtSalonTerminal")]));
        noToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var theirOwner = await factory.SignedInClientAsync(HostB, _polish.Id, Role.Owner);
        using var theirs = await theirOwner.GetAsync(new Uri(LinePath(payment.Id), UriKind.Relative));
        theirs.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var theirPage = await GetAsync(theirOwner, "/admin/payments/cash-up");
        (await PostAsync(theirOwner, LinePath(payment.Id), theirPage, ("method", "CardAtSalonTerminal"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var missing = await owner.GetAsync(new Uri(LinePath(Guid.NewGuid()), UriKind.Relative));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await EntriesAsync()).ShouldHaveSingleItem();
    }

    private static string LinePath(Guid entryId) => $"/admin/payments/corrections/{entryId}";

    private Task<MethodCorrectionResult> CorrectAsync(Guid entryId, PaymentChoice method, Guid? byUserId = null, Guid? correctionId = null, Guid? tenantId = null)
        => ScopedAsync<IMethodCorrectionService, MethodCorrectionResult>(s => s.CorrectAsync(entryId, method, byUserId, correctionId), tenantId);

    private Task<CashDay> TillAsync() => ScopedAsync<ICashUpService, CashDay>(s => s.GetDayAsync(null));

    /// <summary>A visit today, each a little earlier than the last so no two overlap; or so far from now.</summary>
    private Task<Guid> VisitAsync(AppointmentStatus status, Duration? fromNow = null)
        => VisitSeed.AppointmentAsync(factory, _studio.Id, _clientId, _staffId, _serviceId, Now + (fromNow ?? Duration.Zero) - Duration.FromMinutes(35 * ++_visits), status);

    /// <summary>Makes a line one of yesterday's, as if the day had turned since it was recorded.</summary>
    private async Task MoveToYesterdayAsync(Guid entryId)
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ClientAccountEntries
            .Where(e => e.Id == entryId || e.RelatedEntryId == entryId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.At, e => e.At - Duration.FromDays(1)));
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