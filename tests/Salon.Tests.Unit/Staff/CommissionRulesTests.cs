using FsCheck;
using FsCheck.Xunit;
using Salon.Core.Staff;

namespace Salon.Tests.Unit.Staff;

/// <summary>Commission (ADR 0059): a percent from 0 to 100 with two decimals, earned to the penny, half a penny up.</summary>
public class CommissionRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("40", true)]
    [InlineData("12.5", true)]
    [InlineData("33.33", true)]
    [InlineData("100", true)]
    [InlineData("100.01", false)]
    [InlineData("-1", false)]
    [InlineData("12.345", false)]
    public void A_rate_is_0_to_100_with_at_most_two_decimals(string? text, bool expected)
    {
        decimal? rate = text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        CommissionRules.IsRate(rate).ShouldBe(expected);
    }

    [Theory]
    [InlineData("80", "40", "32")]
    [InlineData("59", "10", "5.9")]
    [InlineData("0.05", "10", "0.01")]
    [InlineData("0.04", "10", "0")]
    [InlineData("33.33", "33.33", "11.11")]
    [InlineData("100", "0", "0")]
    public void Commission_is_to_the_penny_half_a_penny_up(string amount, string rate, string expected)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        CommissionRules.Earned(decimal.Parse(amount, inv), decimal.Parse(rate, inv)).ShouldBe(decimal.Parse(expected, inv));
    }

    [Fact]
    public void No_rate_earns_nothing_and_says_so()
    {
        CommissionRules.Earned(100m, null).ShouldBeNull();
        CommissionRules.Total(100m, null, 50m, null).ShouldBeNull();
    }

    [Fact]
    public void Services_and_products_add_up_and_a_missing_rate_counts_as_none()
    {
        CommissionRules.Total(40m, 40m, 59m, 10m).ShouldBe(21.90m);
        CommissionRules.Total(40m, 40m, 59m, null).ShouldBe(16m);
        CommissionRules.Total(40m, null, 59m, 10m).ShouldBe(5.90m);
        CommissionRules.Total(0m, 40m, 0m, null).ShouldBe(0m, "a person paid by commission who earned none earned zero");
    }

    [Fact]
    public void Visits_at_different_rates_are_added_up_and_rounded_once()
    {
        CommissionRules.Earned([(20m, 40m), (20m, 50m)]).ShouldBe(18m);
        CommissionRules.Earned([(0.05m, 10m), (0.05m, 10m), (0.05m, 10m)]).ShouldBe(0.02m, "three half pennies are one and a half pennies, not three");
        CommissionRules.Earned([(20m, 40m), (30m, null)]).ShouldBe(8m, "a visit without a rate earns nothing");
        CommissionRules.Earned([(20m, null)]).ShouldBeNull();
        CommissionRules.Earned([]).ShouldBeNull();
    }

    [Theory]
    [InlineData(true, "40", "50", "40")]
    [InlineData(true, null, "50", null)]
    [InlineData(false, null, "50", "50")]
    [InlineData(false, null, null, null)]
    public void A_visit_earns_at_the_rate_kept_on_it_or_today_if_none_was_kept(bool kept, string? keptRate, string? today, string? expected)
    {
        static decimal? D(string? s) => s is null ? null : decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        CommissionRules.RateFor(kept, D(keptRate), D(today)).ShouldBe(D(expected));
    }

    [Property]
    public void Commission_is_never_more_than_what_it_is_earned_on_nor_below_zero(PositiveInt pennies, byte percent)
    {
        var amount = pennies.Get / 100m;
        var rate = Math.Min(100m, percent);

        var earned = CommissionRules.Earned(amount, rate)!.Value;

        earned.ShouldBeGreaterThanOrEqualTo(0m);
        earned.ShouldBeLessThanOrEqualTo(amount);
        decimal.Round(earned, 2).ShouldBe(earned);
    }
}