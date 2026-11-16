using Salon.Core.Payments;

namespace Salon.Tests.Unit.Payments;

/// <summary>ADR 0053: the rows of the counter sale form as the lines of a sale.</summary>
public class CounterSaleRulesTests
{
    private static readonly Guid Shampoo = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Oil = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Rows_without_a_product_are_no_lines_and_an_empty_quantity_means_one()
    {
        var lines = CounterSaleRules.Lines([(null, 3), (Shampoo, null), (Guid.Empty, 2), (Oil, 2)]);

        lines.ShouldBe([(Shampoo, 1), (Oil, 2)]);
    }

    [Fact]
    public void A_product_chosen_twice_is_one_line_with_the_units_added_up_in_the_order_first_chosen()
    {
        var lines = CounterSaleRules.Lines([(Oil, 1), (Shampoo, 2), (Oil, 3)]);

        lines.ShouldBe([(Oil, 4), (Shampoo, 2)]);
    }

    [Fact]
    public void A_quantity_out_of_range_is_kept_so_it_can_be_refused_by_name()
    {
        CounterSaleRules.Lines([(Shampoo, 0)]).ShouldBe([(Shampoo, 0)]);
        CounterSaleRules.Lines([(Shampoo, 500)]).ShouldBe([(Shampoo, 500)]);
        CounterSaleRules.Lines([]).ShouldBeEmpty();
    }
}