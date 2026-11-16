using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>
/// ADR 0052: what the salon kept of the products it sold. The price less the cost of the day, for the units the
/// client still has; a unit without a cost is counted apart and never guessed at.
/// </summary>
public class ProductProfitRulesTests
{
    [Fact]
    public void The_profit_is_the_price_less_the_cost_for_every_unit_kept()
    {
        var profit = ProductSaleRules.Profit([(3, 12.50m, 6m), (1, 9m, 4m)]);

        profit.Amount.ShouldBe(19.50m + 5m);
        profit.Percent.ShouldBe(52.7m, "24.50 of 46.50");
        profit.UnitsWithoutCost.ShouldBe(0);
    }

    [Fact]
    public void A_unit_without_a_cost_is_counted_apart_and_is_in_neither_figure()
    {
        var profit = ProductSaleRules.Profit([(2, 12.50m, 6m), (4, 18m, null)]);

        profit.Amount.ShouldBe(13m);
        profit.Percent.ShouldBe(52m, "of the 25.00 the costed units were sold for, not of everything");
        profit.UnitsWithoutCost.ShouldBe(4);
    }

    [Fact]
    public void Without_any_cost_there_is_no_profit_to_state()
    {
        var profit = ProductSaleRules.Profit([(2, 12.50m, null)]);

        profit.Amount.ShouldBeNull("not zero: nobody knows");
        profit.Percent.ShouldBeNull();
        profit.UnitsWithoutCost.ShouldBe(2);
        ProductSaleRules.Profit([]).ShouldBe(new RetailProfit(null, null, 0));
    }

    [Fact]
    public void Units_brought_back_earn_nothing()
    {
        var profit = ProductSaleRules.Profit([(2, 12.50m, 6m), (0, 12.50m, 6m), (0, 18m, null)]);

        profit.Amount.ShouldBe(13m);
        profit.UnitsWithoutCost.ShouldBe(0, "a line wholly returned has nothing left to miss a cost");
    }

    [Fact]
    public void Selling_under_cost_is_a_loss_and_giving_away_has_no_share()
    {
        var loss = ProductSaleRules.Profit([(2, 5m, 6m)]);
        var gift = ProductSaleRules.Profit([(1, 0m, 6m)]);

        (loss.Amount, loss.Percent).ShouldBe((-2m, -20m));
        (gift.Amount, gift.Percent).ShouldBe((-6m, null));
    }
}