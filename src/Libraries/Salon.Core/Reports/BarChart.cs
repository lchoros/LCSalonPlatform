namespace Salon.Core.Reports;

/// <summary>One bar in the chart's own units: a rectangle from (X, Y), Y measured from the top.</summary>
/// <param name="Below">True for a value under zero: the bar hangs under the baseline.</param>
public readonly record struct ChartBar(decimal X, decimal Y, decimal Width, decimal Height, bool Below);

/// <param name="Baseline">Where zero is, measured from the top; the bottom edge when no value is under zero.</param>
public sealed record BarChartLayout(int Width, int Height, decimal Baseline, IReadOnlyList<ChartBar> Bars);

/// <summary>
/// Where the bars of a bar chart go (ADR 0048). Pure: numbers in, rectangles out, so a page can draw them as plain
/// SVG shapes without a script. One slot of equal width per value, in order; the tallest value above zero reaches the
/// top, the lowest under zero reaches the bottom, and zero is a line in between.
/// </summary>
public static class BarChart
{
    /// <summary>The share of a slot left empty between two bars.</summary>
    public const decimal GapShare = 0.25m;

    /// <summary>A value that is not zero is drawn at least this tall, so a small day is not mistaken for an empty one.</summary>
    public const decimal MinVisible = 2m;

    public static BarChartLayout Layout(IReadOnlyList<decimal> values, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (values.Count == 0)
        {
            return new BarChartLayout(width, height, height, []);
        }

        var top = Math.Max(0m, values.Max());
        var bottom = Math.Min(0m, values.Min());
        var span = top - bottom;
        var baseline = span == 0m ? height : Round(height * top / span);
        var slot = (decimal)width / values.Count;
        var barWidth = Round(slot * (1m - GapShare));

        var bars = new ChartBar[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            var value = values[i];
            var x = Round((i * slot) + (slot * GapShare / 2m));
            if (value == 0m || span == 0m)
            {
                bars[i] = new ChartBar(x, baseline, barWidth, 0m, false);
                continue;
            }

            var below = value < 0m;
            var room = below ? height - baseline : baseline;
            var tall = Math.Min(room, Math.Max(MinVisible, Round(height * Math.Abs(value) / span)));
            bars[i] = new ChartBar(x, below ? baseline : baseline - tall, barWidth, tall, below);
        }

        return new BarChartLayout(width, height, baseline, bars);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
}