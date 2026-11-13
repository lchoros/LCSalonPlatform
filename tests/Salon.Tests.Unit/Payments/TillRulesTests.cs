using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>How a bill and a day's till add up (ADR 0024): the deposit comes off the bill, money in less money out per method.</summary>
public class TillRulesTests
{
    [Theory]
    [InlineData(45, 0, 45)]
    [InlineData(45, 9, 36)]
    [InlineData(45, 45, 0)]
    [InlineData(45, 60, 0)]
    [InlineData(45, -5, 45)]
    public void What_is_left_of_a_visit_is_the_price_less_the_deposit_held_never_below_zero(decimal price, decimal deposit, decimal expected)
    {
        TillRules.Outstanding(price, deposit).ShouldBe(expected);
    }

    [Theory]
    [InlineData(ClientAccountEntryKind.DepositPaid, 1)]
    [InlineData(ClientAccountEntryKind.FeePayment, 1)]
    [InlineData(ClientAccountEntryKind.VisitPayment, 1)]
    [InlineData(ClientAccountEntryKind.DepositReturned, -1)]
    [InlineData(ClientAccountEntryKind.NoShowFee, 0)]
    [InlineData(ClientAccountEntryKind.LateCancellationFee, 0)]
    [InlineData(ClientAccountEntryKind.FeeWaived, 0)]
    public void Only_lines_that_moved_money_count_and_a_return_counts_against(ClientAccountEntryKind kind, int direction)
    {
        TillRules.Direction(kind).ShouldBe(direction);
    }

    [Fact]
    public void A_day_adds_up_per_method_with_returns_taken_off_and_unused_methods_left_out()
    {
        (ClientAccountEntryKind, PaymentMethod, decimal)[] lines =
        [
            (ClientAccountEntryKind.DepositPaid, PaymentMethod.Cash, 5m),
            (ClientAccountEntryKind.VisitPayment, PaymentMethod.CardAtSalonTerminal, 20m),
            (ClientAccountEntryKind.DepositPaid, PaymentMethod.BankTransfer, 10m),
            (ClientAccountEntryKind.DepositReturned, PaymentMethod.Cash, 10m),
            (ClientAccountEntryKind.FeePayment, PaymentMethod.Cash, 20m),
            (ClientAccountEntryKind.NoShowFee, PaymentMethod.Cash, 99m),
        ];

        TillRules.Totals(lines).ShouldBe(
        [
            (PaymentMethod.Cash, 15m),
            (PaymentMethod.CardAtSalonTerminal, 20m),
            (PaymentMethod.BankTransfer, 10m),
        ]);
        TillRules.ExpectedCash(lines).ShouldBe(15m);
        TillRules.Totals([]).ShouldBeEmpty();
        TillRules.ExpectedCash([]).ShouldBe(0m);
    }

    [Property(MaxTest = 200)]
    public Property The_methods_always_add_up_to_everything_in_less_everything_out()
    {
        var kinds = new[] { ClientAccountEntryKind.DepositPaid, ClientAccountEntryKind.DepositReturned, ClientAccountEntryKind.FeePayment, ClientAccountEntryKind.VisitPayment, ClientAccountEntryKind.FeeWaived };
        var line =
            from kind in Gen.Elements(kinds)
            from method in Gen.Elements(Enum.GetValues<PaymentMethod>())
            from pennies in Gen.Choose(1, 50_000)
            select (Kind: kind, Method: method, Amount: pennies / 100m);

        return Prop.ForAll(Gen.ListOf(line).ToArbitrary(), lines =>
        {
            var net = lines.Sum(l => TillRules.Direction(l.Kind) * l.Amount);
            return TillRules.Totals(lines).Sum(t => t.Amount) == net;
        });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(14.5, true)]
    [InlineData(14.55, true)]
    [InlineData(14.555, false)]
    [InlineData(-0.01, false)]
    [InlineData(1000000.01, false)]
    public void A_count_is_not_negative_and_no_finer_than_a_penny(decimal amount, bool valid)
    {
        TillRules.IsCountable(amount).ShouldBe(valid);
    }
}