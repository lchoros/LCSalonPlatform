using System.ComponentModel;
using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>
/// The salon's own payment methods (ADR 0060): what a form may post as "how was it paid", what a method may be
/// called, which built-in methods a salon may switch off, and how the till adds up a day with own methods in it.
/// </summary>
public class OwnPaymentMethodRulesTests
{
    private static readonly Guid PayPal = Guid.Parse("01938a4e-0000-7000-8000-000000000001");
    private static readonly Guid Amex = Guid.Parse("01938a4e-0000-7000-8000-000000000002");

    [Theory]
    [InlineData("Cash", PaymentMethod.Cash)]
    [InlineData("CardAtSalonTerminal", PaymentMethod.CardAtSalonTerminal)]
    [InlineData(" BankTransfer ", PaymentMethod.BankTransfer)]
    [InlineData("Voucher", PaymentMethod.Voucher)]
    public void A_form_names_a_built_in_method_by_its_name(string posted, PaymentMethod expected)
    {
        PaymentChoice.TryParse(posted, out var choice).ShouldBeTrue();

        choice.ShouldBe(new PaymentChoice(expected));
        choice.IsWellFormed.ShouldBeTrue();
    }

    [Fact]
    public void A_form_names_one_of_the_salons_own_methods_by_its_id()
    {
        PaymentChoice.TryParse(PayPal.ToString(), out var choice).ShouldBeTrue();

        choice.ShouldBe(PaymentChoice.Own(PayPal));
        (choice.Method, choice.CustomMethodId, choice.IsWellFormed, choice.Value).ShouldBe((PaymentMethod.Other, PayPal, true, PayPal.ToString()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Other")]
    [InlineData("cash")]
    [InlineData("1")]
    [InlineData("4")]
    [InlineData("99")]
    [InlineData("Cash,Voucher")]
    [InlineData("PayPal")]
    public void Anything_else_is_not_a_method(string? posted)
    {
        PaymentChoice.TryParse(posted, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_built_in_method_reads_as_a_choice_and_other_needs_its_id()
    {
        PaymentChoice choice = PaymentMethod.Cash;

        (choice.Method, choice.CustomMethodId, choice.Value, choice.ToString()).ShouldBe((PaymentMethod.Cash, null, "Cash", "Cash"));
        new PaymentChoice(PaymentMethod.Other).IsWellFormed.ShouldBeFalse("'other' alone does not say which method");
        new PaymentChoice(PaymentMethod.Cash, PayPal).IsWellFormed.ShouldBeFalse("a built-in method has no id");
        new PaymentChoice((PaymentMethod)42).IsWellFormed.ShouldBeFalse();
        default(PaymentChoice).ShouldBe(new PaymentChoice(PaymentMethod.Cash));
    }

    [Fact]
    public void A_form_field_binds_to_a_choice_and_refuses_nonsense()
    {
        var converter = TypeDescriptor.GetConverter(typeof(PaymentChoice));

        converter.CanConvertFrom(typeof(string)).ShouldBeTrue();
        converter.ConvertFromInvariantString("Voucher").ShouldBe(new PaymentChoice(PaymentMethod.Voucher));
        converter.ConvertFromInvariantString(Amex.ToString()).ShouldBe(PaymentChoice.Own(Amex));
        converter.ConvertToInvariantString(PaymentChoice.Own(Amex)).ShouldBe(Amex.ToString());
        Should.Throw<FormatException>(() => converter.ConvertFromInvariantString("Other"));
    }

    [Theory]
    [InlineData("PayPal", "PayPal")]
    [InlineData("  Second   card  machine ", "Second card machine")]
    [InlineData("\tCheque\n", "Cheque")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_name_loses_the_space_around_it_and_runs_of_space_inside(string? typed, string? expected)
    {
        SalonPaymentMethodRules.Normalize(typed).ShouldBe(expected);
    }

    [Fact]
    public void A_name_is_one_to_forty_characters()
    {
        SalonPaymentMethodRules.IsName("P").ShouldBeTrue();
        SalonPaymentMethodRules.IsName(new string('a', SalonPaymentMethod.NameMaxLength)).ShouldBeTrue();
        SalonPaymentMethodRules.IsName(" " + new string('a', SalonPaymentMethod.NameMaxLength) + " ").ShouldBeTrue("the space around it does not count");
        SalonPaymentMethodRules.IsName(new string('a', SalonPaymentMethod.NameMaxLength + 1)).ShouldBeFalse();
        SalonPaymentMethodRules.IsName("  ").ShouldBeFalse();
        SalonPaymentMethodRules.IsName(null).ShouldBeFalse();
    }

    [Fact]
    public void Two_names_are_the_same_method_whatever_their_capitals_and_spaces()
    {
        SalonPaymentMethodRules.SameName("PayPal", "paypal").ShouldBeTrue();
        SalonPaymentMethodRules.SameName(" Second  card ", "second card").ShouldBeTrue();
        SalonPaymentMethodRules.SameName("PayPal", "Pay Pal").ShouldBeFalse();
    }

    [Fact]
    public void Cash_is_always_offered_the_others_follow_their_switch_and_other_never_alone()
    {
        var all = new PaymentMethodSettings();
        var none = new PaymentMethodSettings { CardAtSalonTerminal = false, BankTransfer = false, Voucher = false };

        SalonPaymentMethodRules.BuiltIn.Where(m => SalonPaymentMethodRules.IsOffered(m, all))
            .ShouldBe([PaymentMethod.Cash, PaymentMethod.CardAtSalonTerminal, PaymentMethod.BankTransfer, PaymentMethod.Voucher]);
        SalonPaymentMethodRules.BuiltIn.Where(m => SalonPaymentMethodRules.IsOffered(m, none)).ShouldBe([PaymentMethod.Cash]);
        SalonPaymentMethodRules.IsOffered(PaymentMethod.Other, all).ShouldBeFalse();
        SalonPaymentMethodRules.Switchable.ShouldNotContain(PaymentMethod.Cash);
        SalonPaymentMethodRules.BuiltIn.ShouldNotContain(PaymentMethod.Other);
    }

    [Fact]
    public void A_voucher_is_paid_for_by_any_method_but_a_voucher()
    {
        GiftVoucherRules.CanBePaidWith(PaymentMethod.Cash).ShouldBeTrue();
        GiftVoucherRules.CanBePaidWith(PaymentChoice.Own(PayPal)).ShouldBeTrue();
        GiftVoucherRules.CanBePaidWith(PaymentMethod.Voucher).ShouldBeFalse();
        GiftVoucherRules.CanBePaidWith(new PaymentChoice(PaymentMethod.Other)).ShouldBeFalse();
        GiftVoucherRules.PaymentMethods.ShouldBe([PaymentMethod.Cash, PaymentMethod.CardAtSalonTerminal, PaymentMethod.BankTransfer]);
    }

    [Fact]
    public void Each_of_the_salons_own_methods_is_its_own_line_after_the_built_in_ones()
    {
        MethodLine[] day =
        [
            new(ClientAccountEntryKind.VisitPayment, PaymentMethod.Other, PayPal, "PayPal", 40m),
            new(ClientAccountEntryKind.VisitPayment, PaymentMethod.Cash, null, null, 20m),
            new(ClientAccountEntryKind.ProductSale, PaymentMethod.Other, Amex, "Amex", 12.50m),
            new(ClientAccountEntryKind.DepositPaid, PaymentMethod.Other, PayPal, "PayPal", 10m),
            new(ClientAccountEntryKind.DepositReturned, PaymentMethod.Other, PayPal, "PayPal", 10m),
            new(ClientAccountEntryKind.NoShowFee, PaymentMethod.Cash, null, null, 99m),
        ];

        TillRules.TotalsByMethod(day).ShouldBe(
        [
            new MethodNet(PaymentMethod.Cash, null, null, 20m),
            new MethodNet(PaymentMethod.Other, Amex, "Amex", 12.50m),
            new MethodNet(PaymentMethod.Other, PayPal, "PayPal", 40m),
        ]);
        TillRules.TotalsByMethod(day).Sum(t => t.Amount).ShouldBe(72.50m, "the lines add up to the day's money");
        TillRules.ExpectedCash(day.Select(l => (l.Kind, l.Method, l.Amount))).ShouldBe(20m, "an own method is never in the drawer");
    }

    [Fact]
    public void A_renamed_method_is_one_line_under_the_name_on_its_latest_line()
    {
        MethodLine[] period =
        [
            new(ClientAccountEntryKind.VisitPayment, PaymentMethod.Other, PayPal, "Paypal", 20m),
            new(ClientAccountEntryKind.VisitPayment, PaymentMethod.Other, PayPal, "PayPal", 30m),
        ];

        TillRules.TotalsByMethod(period).ShouldBe([new MethodNet(PaymentMethod.Other, PayPal, "PayPal", 50m)]);
    }

    [Fact]
    public void Without_own_methods_the_lines_are_the_ones_the_till_always_had()
    {
        (ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)[] day =
        [
            (ClientAccountEntryKind.VisitPayment, PaymentMethod.CardAtSalonTerminal, 45m),
            (ClientAccountEntryKind.DepositPaid, PaymentMethod.Cash, 10m),
            (ClientAccountEntryKind.DepositReturned, PaymentMethod.Cash, 10m),
            (ClientAccountEntryKind.ProductSale, PaymentMethod.BankTransfer, 9m),
        ];

        TillRules.TotalsByMethod(day.Select(l => new MethodLine(l.Kind, l.Method, null, null, l.Amount)))
            .Select(t => (t.Method, t.Amount))
            .ShouldBe(TillRules.Totals(day));
    }
}