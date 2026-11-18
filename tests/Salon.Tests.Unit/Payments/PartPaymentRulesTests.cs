using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>
/// A part of a visit's price paid one way before the rest (ADR 0061): above zero, to the penny, less than what is
/// left; money in on the till; one of the lines that pay for a visit.
/// </summary>
public class PartPaymentRulesTests
{
    [Theory]
    [InlineData("8", "20", true)]
    [InlineData("0.01", "20", true)]
    [InlineData("19.99", "20", true)]
    [InlineData("20", "20", false)]
    [InlineData("20.01", "20", false)]
    [InlineData("0", "20", false)]
    [InlineData("-5", "20", false)]
    [InlineData("7.555", "20", false)]
    [InlineData("0.01", "0.01", false)]
    [InlineData("5", "0", false)]
    public void A_part_is_above_zero_to_the_penny_and_less_than_what_is_left(string amount, string outstanding, bool expected)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        TillRules.IsPart(decimal.Parse(amount, inv), decimal.Parse(outstanding, inv)).ShouldBe(expected);
    }

    [Fact]
    public void A_part_payment_is_money_in_and_belongs_with_the_payment_of_the_visit()
    {
        TillRules.Direction(ClientAccountEntryKind.PartPayment).ShouldBe(1);
        ClientAccountRules.IsVisitPayment(ClientAccountEntryKind.PartPayment).ShouldBeTrue();
        ClientAccountRules.IsDeposit(ClientAccountEntryKind.PartPayment).ShouldBeFalse();
        ClientAccountRules.IsFee(ClientAccountEntryKind.PartPayment).ShouldBeFalse();
        ((int)ClientAccountEntryKind.PartPayment).ShouldBe(13, "the number is in the ledger; it never changes");
    }

    [Fact]
    public void A_day_with_a_part_in_cash_and_the_rest_by_card_adds_up_to_the_price()
    {
        MethodLine[] day =
        [
            new(ClientAccountEntryKind.PartPayment, PaymentMethod.Cash, null, null, 8m),
            new(ClientAccountEntryKind.VisitPayment, PaymentMethod.CardAtSalonTerminal, null, null, 12m),
        ];

        TillRules.TotalsByMethod(day).Select(t => (t.Method, t.Amount)).ShouldBe([(PaymentMethod.Cash, 8m), (PaymentMethod.CardAtSalonTerminal, 12m)]);
        TillRules.ExpectedCash(day.Select(l => (l.Kind, l.Method, l.Amount))).ShouldBe(8m);
        TillRules.Outstanding(20m, 8m).ShouldBe(12m);
    }
}