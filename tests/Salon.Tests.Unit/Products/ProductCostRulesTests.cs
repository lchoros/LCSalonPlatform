using System.Text;
using Salon.Core.Import;
using Salon.Core.Products;
using Salon.Plugin.Import;
using Salon.Services.Clients.Import;
using Salon.Services.Products.Import;

namespace Salon.Tests.Unit.Products;

/// <summary>
/// ADR 0049: what the salon keeps of a product it sells, what its shelves hold in money, and the cost column of a
/// product file. Pure rules.
/// </summary>
public class ProductCostRulesTests
{
    private static readonly IReadOnlySet<string> Nothing = new HashSet<string>(StringComparer.Ordinal);

    [Theory]
    [InlineData(12.50, 6.00, 6.50, 52.0)]
    [InlineData(18.00, 12.00, 6.00, 33.3)]
    [InlineData(10.00, 10.00, 0.00, 0.0)]
    [InlineData(10.00, 0.00, 10.00, 100.0)]
    [InlineData(12.50, 14.00, -1.50, -12.0)]
    public void The_margin_is_the_price_less_the_cost_and_that_as_a_share_of_the_price(double price, double cost, double amount, double percent)
    {
        var margin = ProductRules.Margin((decimal)price, (decimal)cost).ShouldNotBeNull();

        margin.Amount.ShouldBe((decimal)amount);
        margin.Percent.ShouldBe((decimal)percent);
    }

    [Fact]
    public void There_is_no_margin_without_a_price_or_without_a_cost_and_no_share_of_a_price_of_zero()
    {
        ProductRules.Margin(12.50m, null).ShouldBeNull("nobody has said what it costs");
        ProductRules.Margin(null, 6m).ShouldBeNull("a product the salon only uses itself is not sold");
        ProductRules.Margin(0m, 6m).ShouldBe(new ProductMargin(-6m, null), "given away: a loss, and no share of nothing");
    }

    [Fact]
    public void The_shelves_are_worth_their_units_at_cost_and_at_the_prices_to_clients()
    {
        var value = ProductRules.Shelf(
        [
            Product("Shampoo", price: 12.50m, cost: 6m, onHand: 8),
            Product("Colour tube", price: null, cost: 4m, onHand: 10),
            Product("Hair oil", price: 18m, cost: null, onHand: 2),
            Product("Old wax", price: 9m, cost: 3m, onHand: 1, active: false),
            Product("Empty shelf", price: 30m, cost: null, onHand: 0),
        ]);

        value.AtCost.ShouldBe(48m + 40m + 3m, "a product without a cost adds nothing");
        value.AtRetail.ShouldBe(100m + 36m + 9m, "a product that is not for sale adds nothing");
        value.WithoutCost.ShouldBe(1, "the hair oil; an empty shelf has nothing to be worth");
    }

    [Fact]
    public void No_products_are_worth_nothing()
    {
        ProductRules.Shelf([]).ShouldBe(new ShelfValue(0m, 0m, 0));
    }

    [Fact]
    public void The_cost_column_of_a_file_is_read_under_its_English_and_Polish_headings()
    {
        var provider = new CsvProductImportProvider();

        var english = provider.Parse(Encoding.UTF8.GetBytes("Name,Price,Cost price,Stock\nShampoo,12.50,£6.00,8\nHair oil,18,,2\n"));
        var polish = provider.Parse(Encoding.UTF8.GetBytes("Nazwa;Cena;Cena zakupu\nSzampon;45,90;21,50\n"));

        english.Rows.Select(r => (r.Name, r.Price, r.Cost)).ShouldBe([("Shampoo", 12.50m, 6m), ("Hair oil", 18m, null)]);
        polish.Rows.ShouldHaveSingleItem().Cost.ShouldBe(21.50m);
    }

    [Fact]
    public void A_cost_that_is_not_money_or_is_below_zero_makes_the_row_invalid()
    {
        var provider = new CsvProductImportProvider();
        var parsed = provider.Parse(Encoding.UTF8.GetBytes("Name,Cost\nShampoo,six\nHair oil,6.00\n"));
        parsed.Rows[0].Problems.ShouldBe([ProductImportProblem.InvalidCost]);
        parsed.Rows[1].Problems.ShouldBeEmpty();

        var rows = ProductImportClassifier.Classify(
        [
            parsed.Rows[0],
            new ImportedProductRow(3, "Wax", null, null, null, null, null, []) { Cost = -1m },
            new ImportedProductRow(4, "Serum", null, null, null, null, null, []) { Cost = 1.999m },
            parsed.Rows[1],
        ], Nothing);

        rows.Select(r => r.Status).ShouldBe([ImportRowStatus.Invalid, ImportRowStatus.Invalid, ImportRowStatus.Invalid, ImportRowStatus.New]);
        rows.Take(3).ShouldAllBe(r => r.Problems.Single() == ProductImportProblem.InvalidCost);
    }

    private static Product Product(string name, decimal? price, decimal? cost, int onHand, bool active = true)
        => new() { Name = name, Currency = "GBP", RetailPrice = price, CostPrice = cost, StockOnHand = onHand, IsActive = active };
}