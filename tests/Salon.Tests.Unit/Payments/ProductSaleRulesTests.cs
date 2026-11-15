using Salon.Core.Payments;
using Salon.Core.Products;

namespace Salon.Tests.Unit.Payments;

/// <summary>What may go on a bill and how its product lines add up (ADR 0037).</summary>
public class ProductSaleRulesTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(99, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(100, false)]
    public void A_bill_line_holds_between_one_and_ninety_nine_units(int quantity, bool expected)
    {
        ProductSaleRules.IsQuantity(quantity).ShouldBe(expected);
    }

    [Theory]
    [InlineData(true, "12.50", true)]
    [InlineData(true, null, false)]
    [InlineData(true, "0", false)]
    [InlineData(false, "12.50", false)]
    public void Only_a_product_in_use_with_a_price_above_zero_is_sold(bool active, string? price, bool expected)
    {
        var product = new Product
        {
            Name = "Shampoo 250 ml",
            Currency = "GBP",
            IsActive = active,
            RetailPrice = price is null ? null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture),
        };

        ProductSaleRules.IsForSale(product).ShouldBe(expected);
    }

    [Fact]
    public void A_line_costs_its_quantity_times_the_price_of_one()
    {
        ProductSaleRules.LineTotal(3, 12.50m).ShouldBe(37.50m);
        new VisitProductLine { ProductName = "Shampoo 250 ml", Currency = "GBP", Quantity = 2, UnitPrice = 13.50m }.Total.ShouldBe(new Salon.Core.Domain.Money(27m, "GBP"));
    }

    [Fact]
    public void Only_unpaid_lines_are_left_to_pay()
    {
        (int, decimal, bool)[] lines = [(2, 12.50m, false), (1, 18m, true), (1, 14m, false)];

        ProductSaleRules.Outstanding(lines).ShouldBe(39m);
        ProductSaleRules.Outstanding([]).ShouldBe(0m);
    }

    [Theory]
    [InlineData(5, 2, 0)]
    [InlineData(2, 2, 0)]
    [InlineData(1, 2, 1)]
    [InlineData(0, 3, 3)]
    public void A_sale_counts_the_shelf_up_by_exactly_what_the_record_is_short(int onHand, int quantity, int expected)
    {
        StockRules.ShortFor(onHand, quantity).ShouldBe(expected);
        // After the count the sale always fits and never leaves the shelf below zero.
        StockRules.Fits(onHand + expected, -quantity).ShouldBeTrue();
    }

    [Fact]
    public void A_sale_takes_from_the_shelf()
    {
        StockRules.Direction(StockMovementKind.Sold).ShouldBe(-1);
        StockRules.Change(StockMovementKind.Sold, 2, 10).ShouldBe(-2);
        StockRules.NeedsNote(StockMovementKind.Sold).ShouldBeFalse();
    }
}