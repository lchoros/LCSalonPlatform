using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Salon.Core.Reports;

namespace Salon.Tests.Unit.Reports;

/// <summary>
/// ADR 0048: where the bars of the takings chart go. Pure geometry: one slot per value, the best day reaches the top,
/// a day with more money out than in hangs under the line, and nothing is ever drawn outside the box.
/// </summary>
public class BarChartTests
{
    [Fact]
    public void The_highest_value_reaches_the_top_and_the_others_are_in_proportion()
    {
        var layout = BarChart.Layout([50m, 200m, 0m, 100m], 400, 100);

        layout.Baseline.ShouldBe(100m, "nothing is under zero, so zero is the bottom edge");
        layout.Bars.Select(b => b.Height).ShouldBe([25m, 100m, 0m, 50m]);
        layout.Bars.Select(b => b.Y).ShouldBe([75m, 0m, 100m, 50m]);
        layout.Bars.ShouldAllBe(b => !b.Below);
    }

    [Fact]
    public void Every_value_gets_a_slot_of_the_same_width_with_a_gap_between_the_bars()
    {
        var layout = BarChart.Layout([1m, 1m, 1m, 1m], 400, 100);

        // A slot is 100 wide; a quarter of it is the gap, half on each side of the bar.
        layout.Bars.Select(b => b.Width).ShouldAllBe(w => w == 75m);
        layout.Bars.Select(b => b.X).ShouldBe([12.5m, 112.5m, 212.5m, 312.5m]);
    }

    [Fact]
    public void A_day_with_more_money_out_than_in_hangs_under_the_line()
    {
        var layout = BarChart.Layout([150m, -50m], 200, 100);

        layout.Baseline.ShouldBe(75m, "three quarters of the height are above zero");
        layout.Bars[0].ShouldBe(new ChartBar(12.5m, 0m, 75m, 75m, false));
        layout.Bars[1].ShouldBe(new ChartBar(112.5m, 75m, 75m, 25m, true));
    }

    [Fact]
    public void Only_money_out_puts_the_line_at_the_top()
    {
        var layout = BarChart.Layout([-20m, -40m], 200, 100);

        layout.Baseline.ShouldBe(0m);
        layout.Bars.Select(b => b.Height).ShouldBe([50m, 100m]);
        layout.Bars.ShouldAllBe(b => b.Below && b.Y == 0m);
    }

    [Fact]
    public void A_small_day_beside_a_big_one_can_still_be_seen_and_an_empty_day_cannot()
    {
        var layout = BarChart.Layout([5m, 0m, 5000m], 300, 100);

        layout.Bars[0].Height.ShouldBe(BarChart.MinVisible);
        layout.Bars[0].Y.ShouldBe(100m - BarChart.MinVisible);
        layout.Bars[1].Height.ShouldBe(0m);
    }

    [Fact]
    public void Nothing_at_all_is_a_flat_line_at_the_bottom_and_no_values_are_no_bars()
    {
        var flat = BarChart.Layout([0m, 0m, 0m], 300, 100);

        flat.Baseline.ShouldBe(100m);
        flat.Bars.Count.ShouldBe(3);
        flat.Bars.ShouldAllBe(b => b.Height == 0m);
        BarChart.Layout([], 300, 100).Bars.ShouldBeEmpty();
    }

    [Fact]
    public void A_box_without_a_size_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => BarChart.Layout([1m], 0, 100));
        Should.Throw<ArgumentOutOfRangeException>(() => BarChart.Layout([1m], 100, 0));
    }

    [Property]
    public Property No_bar_ever_leaves_the_box_and_every_bar_touches_the_line(int[] pence)
    {
        var values = (pence ?? []).Take(62).Select(p => p / 100m).ToArray();

        var layout = BarChart.Layout(values, 620, 160);

        return (layout.Bars.Count == values.Length
            && layout.Baseline >= 0m && layout.Baseline <= 160m
            && layout.Bars.All(b => b.X >= 0m && b.X + b.Width <= 620.01m && b.Y >= 0m && b.Y + b.Height <= 160.01m)
            && layout.Bars.Where(b => b.Below).All(b => b.Y == layout.Baseline)
            && layout.Bars.Where(b => !b.Below).All(b => b.Y + b.Height == layout.Baseline)
            && layout.Bars.Zip(values).All(pair => (pair.Second == 0m) == (pair.First.Height == 0m) || layout.Baseline is 0m or 160m)).ToProperty();
    }
}