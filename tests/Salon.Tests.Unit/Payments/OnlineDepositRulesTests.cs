using Salon.Core.Domain;
using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>
/// What the ledger does with a deposit a gateway says was paid online (ADR 0082): one line per session however often
/// it is reported, only for exactly what the booking asked for, and never from a form at the desk.
/// </summary>
public class OnlineDepositRulesTests
{
    private const string Session = "cs_test_unit_one";
    private static readonly Guid Tenant = Guid.CreateVersion7();
    private static readonly Guid Appointment = Guid.CreateVersion7();
    private static readonly Money Due = new(9m, "GBP");

    private static OnlineDepositPayment Paid(decimal amount = 9m, string currency = "GBP", string reference = Session, string provider = "Payments.Stripe")
        => new(Tenant, Appointment, new Money(amount, currency), provider, reference);

    [Fact]
    public void Exactly_the_deposit_asked_for_is_recorded()
    {
        OnlineDepositRules.Decide(Paid(), Due, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.Recorded);
    }

    [Fact]
    public void The_same_session_again_is_the_line_that_is_already_there()
    {
        // The client's browser comes back, then the webhook arrives, then the client reloads the page.
        for (var report = 0; report < 3; report++)
        {
            OnlineDepositRules.Decide(Paid(), Due, hasDeposit: true, recordedReference: Session).ShouldBe(OnlineDepositOutcome.AlreadyRecorded);
        }
    }

    [Theory]
    [InlineData("cs_test_unit_two")]
    [InlineData("manual:deposit:0190")]
    [InlineData(null)]
    public void A_visit_that_has_a_deposit_from_elsewhere_gets_no_second_one(string? recordedReference)
    {
        OnlineDepositRules.Decide(Paid(), Due, hasDeposit: true, recordedReference).ShouldBe(OnlineDepositOutcome.AnotherDepositRecorded);
    }

    [Fact]
    public void A_session_id_is_compared_letter_for_letter()
    {
        OnlineDepositRules.Decide(Paid(reference: Session.ToUpperInvariant()), Due, hasDeposit: true, recordedReference: Session)
            .ShouldBe(OnlineDepositOutcome.AnotherDepositRecorded);
    }

    [Theory]
    [InlineData(8.99)]
    [InlineData(9.01)]
    [InlineData(90)]
    [InlineData(0.09)]
    public void Money_that_is_not_the_amount_asked_for_is_left_for_a_person(decimal amount)
    {
        OnlineDepositRules.Decide(Paid(amount), Due, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.AmountMismatch);
    }

    [Theory]
    [InlineData("PLN")]
    [InlineData("EUR")]
    [InlineData("gbp")]
    public void Money_in_another_currency_is_never_read_as_the_bookings_own(string currency)
    {
        OnlineDepositRules.Decide(Paid(currency: currency), Due, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.CurrencyMismatch);
    }

    [Fact]
    public void A_booking_that_asks_for_no_deposit_gets_none()
    {
        OnlineDepositRules.Decide(Paid(), due: null, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.NoDepositAsked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_payment_without_a_reference_cannot_be_told_from_the_next_one(string reference)
    {
        OnlineDepositRules.Decide(Paid(reference: reference), Due, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.ReferenceUnusable);
        OnlineDepositRules.Decide(Paid(provider: reference), Due, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.ReferenceUnusable);
    }

    [Fact]
    public void A_reference_too_long_to_keep_whole_is_refused_rather_than_cut()
    {
        var longest = new string('a', ClientAccountEntry.ProviderReferenceMaxLength);

        OnlineDepositRules.IsReference(longest).ShouldBeTrue();
        OnlineDepositRules.IsReference(longest + "a").ShouldBeFalse();
        OnlineDepositRules.Decide(Paid(reference: longest + "a"), Due, hasDeposit: false, recordedReference: null).ShouldBe(OnlineDepositOutcome.ReferenceUnusable);
    }

    [Fact]
    public void The_online_card_method_is_written_by_a_gateway_and_never_chosen_at_the_desk()
    {
        SalonPaymentMethodRules.IsGatewayOnly(PaymentMethod.OnlineCard).ShouldBeTrue();
        SalonPaymentMethodRules.BuiltIn.ShouldNotContain(PaymentMethod.OnlineCard);
        SalonPaymentMethodRules.Switchable.ShouldNotContain(PaymentMethod.OnlineCard);
        SalonPaymentMethodRules.IsOffered(PaymentMethod.OnlineCard, new PaymentMethodSettings()).ShouldBeFalse();

        // A form that posts it, by name or by number, is refused like a method that does not exist.
        PaymentChoice.TryParse("OnlineCard", out _).ShouldBeFalse();
        PaymentChoice.TryParse("5", out _).ShouldBeFalse();
        new PaymentChoice(PaymentMethod.OnlineCard).IsWellFormed.ShouldBeFalse();
        GiftVoucherRules.CanBePaidWith(new PaymentChoice(PaymentMethod.OnlineCard)).ShouldBeFalse();
        GiftVoucherRules.PaymentMethods.ShouldNotContain(PaymentMethod.OnlineCard);
        MethodCorrectionRules.Allows(ClientAccountEntryKind.DepositPaid, new PaymentChoice(PaymentMethod.OnlineCard)).ShouldBeFalse();

        // And what a gateway wrote is not "put right" to another method afterwards; a line the desk recorded still is.
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.DepositPaid, PaymentMethod.OnlineCard).ShouldBeFalse();
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.DepositPaid, PaymentMethod.Cash).ShouldBeTrue();
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.DepositPaid, null).ShouldBeFalse();

        // The methods the desk has always had are untouched.
        foreach (var method in SalonPaymentMethodRules.BuiltIn)
        {
            SalonPaymentMethodRules.IsGatewayOnly(method).ShouldBeFalse();
            new PaymentChoice(method).IsWellFormed.ShouldBeTrue();
        }
    }

    [Fact]
    public void An_online_deposit_is_money_in_but_never_cash_in_the_drawer()
    {
        (ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)[] day =
        [
            (ClientAccountEntryKind.DepositPaid, PaymentMethod.OnlineCard, 9m),
            (ClientAccountEntryKind.VisitPayment, PaymentMethod.Cash, 36m),
        ];

        TillRules.Totals(day).ShouldBe([(PaymentMethod.Cash, 36m), (PaymentMethod.OnlineCard, 9m)]);
        TillRules.ExpectedCash(day).ShouldBe(36m);
    }

    [Fact]
    public void A_host_without_a_gateway_takes_no_money_and_reads_nothing()
    {
        var none = new NoOnlineDepositCheckout();

        none.IsAvailable.ShouldBeFalse();
        none.ReadWebhook("{}", "t=1,v1=00").State.ShouldBe(OnlineWebhookState.NotConfigured);
    }

    [Fact]
    public async Task A_host_without_a_gateway_opens_no_payment_page()
    {
        var none = new NoOnlineDepositCheckout();

        (await none.StartAsync(new OnlineCheckoutRequest(Tenant, Appointment, Due, "Deposit", "https://salon.example/back", "https://salon.example/done"))).RedirectUrl.ShouldBeNull();
        (await none.ReadAsync(Session)).State.ShouldBe(OnlineCheckoutState.Unavailable);
    }
}