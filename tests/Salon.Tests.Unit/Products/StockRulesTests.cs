using FsCheck.Xunit;
using Salon.Core.Products;

namespace Salon.Tests.Unit.Products;

/// <summary>How a shelf adds up (ADR 0036): plain numbers in, signed changes out, never below zero.</summary>
public class StockRulesTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(10_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(10_001, false)]
    public void A_moved_quantity_is_a_whole_number_of_units_above_zero(int quantity, bool expected)
    {
        StockRules.IsMovable(quantity).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1_000_000, true)]
    [InlineData(-1, false)]
    [InlineData(1_000_001, false)]
    public void A_counted_shelf_is_zero_or_more(int counted, bool expected)
    {
        StockRules.IsCountable(counted).ShouldBe(expected);
    }

    [Theory]
    [InlineData(StockMovementKind.Received, 6, 10, 6)]
    [InlineData(StockMovementKind.UsedInSalon, 2, 10, -2)]
    [InlineData(StockMovementKind.WrittenOff, 1, 10, -1)]
    [InlineData(StockMovementKind.Counted, 7, 10, -3)]
    [InlineData(StockMovementKind.Counted, 12, 10, 2)]
    [InlineData(StockMovementKind.Counted, 10, 10, 0)]
    public void The_kind_decides_what_a_typed_number_does_to_the_shelf(StockMovementKind kind, int number, int onHand, int expected)
    {
        StockRules.Change(kind, number, onHand).ShouldBe(expected);
    }

    [Theory]
    [InlineData(3, -3, true)]
    [InlineData(3, -4, false)]
    [InlineData(0, -1, false)]
    [InlineData(999_999, 1, true)]
    [InlineData(999_999, 2, false)]
    [InlineData(1_000_000, int.MaxValue, false)]
    public void The_shelf_never_goes_below_zero_or_above_its_limit(int onHand, int change, bool expected)
    {
        StockRules.Fits(onHand, change).ShouldBe(expected);
    }

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(1, 2, true)]
    [InlineData(3, 2, false)]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    public void A_shelf_at_or_under_its_threshold_is_low(int onHand, int lowStockAt, bool expected)
    {
        StockRules.IsLow(onHand, lowStockAt).ShouldBe(expected);
    }

    [Fact]
    public void A_product_without_a_threshold_is_never_low()
    {
        StockRules.IsLow(0, null).ShouldBeFalse();
    }

    [Fact]
    public void Only_a_write_off_must_say_why()
    {
        Enum.GetValues<StockMovementKind>().Where(StockRules.NeedsNote).ShouldBe([StockMovementKind.WrittenOff]);
    }

    [Fact]
    public void Every_kind_but_a_count_has_a_direction()
    {
        foreach (var kind in Enum.GetValues<StockMovementKind>())
        {
            (StockRules.Direction(kind) == 0).ShouldBe(kind == StockMovementKind.Counted, kind.ToString());
        }
    }

    [Property]
    public bool A_count_always_leaves_the_shelf_at_the_counted_number(ushort onHand, ushort counted)
        => onHand + StockRules.Change(StockMovementKind.Counted, counted, onHand) == counted;

    [Property]
    public bool Accepted_changes_keep_the_running_total_equal_to_the_sum_of_the_lines(int[] numbers)
    {
        // Walk a shelf through arbitrary deliveries and uses; refused changes add no line.
        var onHand = 0;
        var lines = new List<int>();
        foreach (var number in numbers ?? [])
        {
            var kind = number >= 0 ? StockMovementKind.Received : StockMovementKind.UsedInSalon;
            var quantity = Math.Abs(number % (StockRules.MaxQuantity + 1));
            if (!StockRules.IsMovable(quantity))
            {
                continue;
            }

            var change = StockRules.Change(kind, quantity, onHand);
            if (!StockRules.Fits(onHand, change))
            {
                continue;
            }

            onHand += change;
            lines.Add(change);
        }

        return onHand >= 0 && onHand == StockRules.OnHand(lines);
    }
}