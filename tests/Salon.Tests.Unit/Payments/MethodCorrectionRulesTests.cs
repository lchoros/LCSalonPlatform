using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>
/// Putting right a line recorded under the wrong method (ADR 0064): two lines that move its amount from one method
/// to another and add nothing to the day.
/// </summary>
public class MethodCorrectionRulesTests
{
    private static readonly ClientAccountEntryKind[] MoneyIn =
    [
        ClientAccountEntryKind.DepositPaid, ClientAccountEntryKind.FeePayment, ClientAccountEntryKind.VisitPayment,
        ClientAccountEntryKind.PartPayment, ClientAccountEntryKind.ProductSale, ClientAccountEntryKind.VoucherSale,
    ];

    private static readonly ClientAccountEntryKind[] MoneyOut =
    [
        ClientAccountEntryKind.DepositReturned, ClientAccountEntryKind.ProductReturn, ClientAccountEntryKind.VoucherRefund,
    ];

    [Fact]
    public void A_line_that_moved_money_can_be_corrected_and_no_other()
    {
        foreach (var kind in Enum.GetValues<ClientAccountEntryKind>())
        {
            MethodCorrectionRules.CanCorrect(kind).ShouldBe(MoneyIn.Contains(kind) || MoneyOut.Contains(kind), kind.ToString());
        }

        // A fee added or waived and a voucher used moved nothing; a correction is not corrected, the line is corrected again.
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.NoShowFee).ShouldBeFalse();
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.VoucherRedeemed).ShouldBeFalse();
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.MethodCorrectionIn).ShouldBeFalse();
        MethodCorrectionRules.CanCorrect(ClientAccountEntryKind.MethodCorrectionOut).ShouldBeFalse();
    }

    [Fact]
    public void Money_that_came_in_is_taken_off_one_method_and_put_on_another_and_money_that_went_out_the_other_way_round()
    {
        foreach (var kind in MoneyIn)
        {
            MethodCorrectionRules.Pair(kind).ShouldBe((ClientAccountEntryKind.MethodCorrectionOut, ClientAccountEntryKind.MethodCorrectionIn), kind.ToString());
        }

        foreach (var kind in MoneyOut)
        {
            MethodCorrectionRules.Pair(kind).ShouldBe((ClientAccountEntryKind.MethodCorrectionIn, ClientAccountEntryKind.MethodCorrectionOut), kind.ToString());
        }

        Should.Throw<ArgumentOutOfRangeException>(() => MethodCorrectionRules.Pair(ClientAccountEntryKind.FeeWaived));
        Should.Throw<ArgumentOutOfRangeException>(() => MethodCorrectionRules.Pair(ClientAccountEntryKind.MethodCorrectionIn));
    }

    [Fact]
    public void The_two_halves_count_one_each_way_and_are_a_family_of_their_own()
    {
        TillRules.Direction(ClientAccountEntryKind.MethodCorrectionIn).ShouldBe(1);
        TillRules.Direction(ClientAccountEntryKind.MethodCorrectionOut).ShouldBe(-1);
        ClientAccountRules.IsMethodCorrection(ClientAccountEntryKind.MethodCorrectionIn).ShouldBeTrue();
        ClientAccountRules.IsMethodCorrection(ClientAccountEntryKind.MethodCorrectionOut).ShouldBeTrue();
        ClientAccountRules.IsMethodCorrection(ClientAccountEntryKind.VisitPayment).ShouldBeFalse();
    }

    [Fact]
    public void The_half_that_moves_money_the_same_way_as_the_line_says_how_it_was_paid()
    {
        foreach (var kind in MoneyIn)
        {
            MethodCorrectionRules.Restates(kind, ClientAccountEntryKind.MethodCorrectionIn).ShouldBeTrue(kind.ToString());
            MethodCorrectionRules.Restates(kind, ClientAccountEntryKind.MethodCorrectionOut).ShouldBeFalse(kind.ToString());
        }

        foreach (var kind in MoneyOut)
        {
            MethodCorrectionRules.Restates(kind, ClientAccountEntryKind.MethodCorrectionOut).ShouldBeTrue(kind.ToString());
            MethodCorrectionRules.Restates(kind, ClientAccountEntryKind.MethodCorrectionIn).ShouldBeFalse(kind.ToString());
        }

        // Not a correction, or not a line that can have one.
        MethodCorrectionRules.Restates(ClientAccountEntryKind.VisitPayment, ClientAccountEntryKind.PartPayment).ShouldBeFalse();
        MethodCorrectionRules.Restates(ClientAccountEntryKind.NoShowFee, ClientAccountEntryKind.MethodCorrectionIn).ShouldBeFalse();
        MethodCorrectionRules.Restates(ClientAccountEntryKind.MethodCorrectionIn, ClientAccountEntryKind.MethodCorrectionIn).ShouldBeFalse();
    }

    [Fact]
    public void A_gift_vouchers_own_money_is_never_moved_to_voucher_and_any_other_line_may_be()
    {
        MethodCorrectionRules.Allows(ClientAccountEntryKind.VoucherSale, PaymentMethod.Voucher).ShouldBeFalse();
        MethodCorrectionRules.Allows(ClientAccountEntryKind.VoucherRefund, PaymentMethod.Voucher).ShouldBeFalse();
        MethodCorrectionRules.Allows(ClientAccountEntryKind.VoucherSale, PaymentMethod.CardAtSalonTerminal).ShouldBeTrue();
        MethodCorrectionRules.Allows(ClientAccountEntryKind.VoucherSale, PaymentChoice.Own(Guid.NewGuid())).ShouldBeTrue();
        MethodCorrectionRules.Allows(ClientAccountEntryKind.VisitPayment, PaymentMethod.Voucher).ShouldBeTrue();

        // "Other" without saying which of the salon's own methods is not a method.
        MethodCorrectionRules.Allows(ClientAccountEntryKind.VisitPayment, PaymentMethod.Other).ShouldBeFalse();
    }

    [Property(MaxTest = 200)]
    public Property A_correction_never_changes_the_days_takings_and_moves_exactly_the_lines_amount()
    {
        var methods = new[] { PaymentMethod.Cash, PaymentMethod.CardAtSalonTerminal, PaymentMethod.BankTransfer };
        var correction =
            from kind in Gen.Elements(MoneyIn.Concat(MoneyOut).ToArray())
            from recorded in Gen.Elements(methods)
            from paidBy in Gen.Elements(methods)
            from pennies in Gen.Choose(1, 50_000)
            select (Kind: kind, Recorded: recorded, PaidBy: paidBy, Amount: pennies / 100m);

        return Prop.ForAll(correction.ToArbitrary(), c =>
        {
            var (undo, restate) = MethodCorrectionRules.Pair(c.Kind);
            (ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)[] asRecorded = [(c.Kind, c.Recorded, c.Amount)];
            (ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)[] corrected = [.. asRecorded, (undo, c.Recorded, c.Amount), (restate, c.PaidBy, c.Amount)];
            (ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)[] asPaid = [(c.Kind, c.PaidBy, c.Amount)];

            // The till after the correction is the till the day would have had with the right method from the start.
            decimal By(IEnumerable<(ClientAccountEntryKind Kind, PaymentMethod Method, decimal Amount)> lines, PaymentMethod method)
                => lines.Where(l => l.Method == method).Sum(l => TillRules.Direction(l.Kind) * l.Amount);
            return methods.All(m => By(corrected, m) == By(asPaid, m))
                && corrected.Sum(l => TillRules.Direction(l.Kind) * l.Amount) == asRecorded.Sum(l => TillRules.Direction(l.Kind) * l.Amount)
                && TillRules.ExpectedCash(corrected) == TillRules.ExpectedCash(asPaid);
        });
    }
}