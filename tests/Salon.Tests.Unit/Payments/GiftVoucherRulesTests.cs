using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>
/// ADR 0057: what a gift voucher may be worth, how its code is drawn, typed and printed, when it can be used, how
/// much of it goes onto a bill and when the money can be given back. Pure rules.
/// </summary>
public class GiftVoucherRulesTests
{
    private static readonly LocalDate Today = new(2026, 11, 16);
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 16, 12, 0);

    [Theory]
    [InlineData(0.01, true)]
    [InlineData(50, true)]
    [InlineData(5000, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(5000.01, false)]
    [InlineData(12.345, false)]
    public void A_voucher_is_sold_for_an_amount_of_money_above_zero_up_to_the_cap(double amount, bool valid)
    {
        GiftVoucherRules.IsAmount((decimal)amount).ShouldBe(valid);
    }

    [Fact]
    public void A_code_is_ten_characters_nobody_mixes_up_when_reading_it_aloud()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => GiftVoucherRules.NewCode()).ToArray();

        codes.ShouldAllBe(c => c.Length == GiftVoucher.CodeLength && c.All(ch => GiftVoucherRules.Alphabet.Contains(ch, StringComparison.Ordinal)));
        codes.Distinct().Count().ShouldBe(200, "ten characters of thirty-one do not repeat in two hundred draws");
        GiftVoucherRules.Alphabet.ShouldNotContain('0');
        GiftVoucherRules.Alphabet.ShouldNotContain('O');
        GiftVoucherRules.Alphabet.ShouldNotContain('1');
        GiftVoucherRules.Alphabet.ShouldNotContain('I');
        GiftVoucherRules.Alphabet.ShouldNotContain('L');
    }

    [Theory]
    [InlineData("ABCDE-FGHJK", "ABCDEFGHJK")]
    [InlineData("abcde fghjk", "ABCDEFGHJK")]
    [InlineData("  ab-cd-ef-gh-jk  ", "ABCDEFGHJK")]
    [InlineData("23456789AB", "23456789AB")]
    public void A_code_is_read_however_it_was_typed(string typed, string code)
    {
        GiftVoucherRules.Normalize(typed).ShouldBe(code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABCDE")]
    [InlineData("ABCDE-FGHJK2")]
    [InlineData("ABCDE-FGHJ0")]
    [InlineData("ABCDE-FGHJI")]
    [InlineData("ABCDE_FGHJK")]
    public void What_cannot_be_a_code_is_no_code(string? typed)
    {
        GiftVoucherRules.Normalize(typed).ShouldBeNull();
    }

    [Property]
    public Property A_code_printed_and_typed_back_is_the_same_code()
    {
        return Prop.ForAll(Gen.Constant(0).Select(_ => GiftVoucherRules.NewCode()).ToArbitrary(), code =>
            GiftVoucherRules.Format(code).Length == 11 && GiftVoucherRules.Normalize(GiftVoucherRules.Format(code)) == code);
    }

    [Fact]
    public void A_voucher_is_good_for_its_months_through_its_last_day_and_zero_months_never_ends()
    {
        GiftVoucherRules.ExpiresOn(Today, 12).ShouldBe(new LocalDate(2027, 11, 16));
        GiftVoucherRules.ExpiresOn(Today, 0).ShouldBeNull();
        GiftVoucherRules.IsExpired(Today, Today).ShouldBeFalse("its last day is still a day it can be used");
        GiftVoucherRules.IsExpired(Today.PlusDays(-1), Today).ShouldBeTrue();
        GiftVoucherRules.IsExpired(null, Today.PlusYears(50)).ShouldBeFalse();
    }

    [Fact]
    public void A_voucher_is_active_used_up_expired_or_given_back()
    {
        GiftVoucherRules.Status(50m, Today.PlusDays(1), null, Today).ShouldBe(GiftVoucherStatus.Active);
        GiftVoucherRules.Status(0m, Today.PlusDays(1), null, Today).ShouldBe(GiftVoucherStatus.Used);
        GiftVoucherRules.Status(50m, Today.PlusDays(-1), null, Today).ShouldBe(GiftVoucherStatus.Expired);
        GiftVoucherRules.Status(0m, Today.PlusDays(-1), null, Today).ShouldBe(GiftVoucherStatus.Used, "used up before it ran out");
        GiftVoucherRules.Status(0m, null, Now, Today).ShouldBe(GiftVoucherStatus.Refunded, "given back, not used up");
    }

    [Theory]
    [InlineData(50, 20, 20)]
    [InlineData(15, 20, 15)]
    [InlineData(20, 20, 20)]
    [InlineData(50, 0, 0)]
    [InlineData(0, 20, 0)]
    public void What_goes_onto_a_bill_is_what_is_left_to_pay_or_all_the_voucher_has(double balance, double outstanding, double applies)
    {
        GiftVoucherRules.Applies((decimal)balance, (decimal)outstanding).ShouldBe((decimal)applies);
    }

    [Fact]
    public void The_money_goes_back_only_for_a_voucher_nobody_has_touched()
    {
        GiftVoucherRules.CanRefund(50m, 50m, null).ShouldBeTrue();
        GiftVoucherRules.CanRefund(50m, 30m, null).ShouldBeFalse("part of it was used");
        GiftVoucherRules.CanRefund(50m, 0m, null).ShouldBeFalse();
        GiftVoucherRules.CanRefund(50m, 50m, Now).ShouldBeFalse("given back already");
    }

    [Fact]
    public void A_voucher_is_bought_and_given_back_with_money_never_with_a_voucher()
    {
        GiftVoucherRules.PaymentMethods.ShouldNotContain(PaymentMethod.Voucher);
        GiftVoucherRules.PaymentMethods.ShouldContain(PaymentMethod.Cash);
        TillRules.Direction(ClientAccountEntryKind.VoucherSale).ShouldBe(1);
        TillRules.Direction(ClientAccountEntryKind.VoucherRefund).ShouldBe(-1);
        TillRules.Direction(ClientAccountEntryKind.VoucherRedeemed).ShouldBe(0, "its money came in when it was sold");
    }
}