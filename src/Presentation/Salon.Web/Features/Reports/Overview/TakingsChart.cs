using System.Globalization;
using Salon.Core.Reports;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Reports;

namespace Salon.Web.Features.Reports.Overview;

/// <summary>
/// One bar of the takings chart as the view writes it (ADR 0048). The numbers are text with a point as the decimal
/// sign whatever the reader's language: they are SVG attributes, and "12,5" is not a number there.
/// </summary>
/// <param name="Day">ISO date, for tests and for whoever reads the markup.</param>
/// <param name="Title">"Saturday 14 November 2026: £320.00", shown when the bar is pointed at.</param>
public sealed record TakingsChartBar(string X, string Y, string Width, string Height, bool Below, string Day, string Title);

/// <param name="Summary">One sentence that says what the picture shows, for a screen reader and under the chart.</param>
public sealed record TakingsChart(string ViewBox, string Width, string Baseline, IReadOnlyList<TakingsChartBar> Bars, string First, string Last, string Summary);

/// <summary>
/// The takings of each day as bars (ADR 0048, ADR 0043): the geometry is <see cref="BarChart"/>'s, the words are
/// written here, and the view only writes the shapes down. No script and no style attribute.
/// </summary>
public static class TakingsCharts
{
    /// <summary>The chart's own units; the page stretches the picture to the width it has.</summary>
    public const int Width = 620;

    public const int Height = 160;

    private const string EdgePattern = "d MMM";

    /// <summary>Null when the period is not drawn: no money, a single day, or too many days for bars.</summary>
    public static TakingsChart? For(TakingsReport takings, ILocalizationService localizer, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(takings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        var days = takings.Daily;
        if (days.Count == 0)
        {
            return null;
        }

        var layout = BarChart.Layout([.. days.Select(d => d.Amount.Amount)], Width, Height);
        var bars = days
            .Zip(layout.Bars, (day, bar) => new TakingsChartBar(
                Number(bar.X),
                Number(bar.Y),
                Number(bar.Width),
                Number(bar.Height),
                bar.Below,
                day.Day.ToIso(),
                localizer.Format("Reports.Chart.Bar", day.Day.ToDayHeading(culture), day.Amount.ToDisplayString(culture))))
            .ToArray();
        var best = days.MaxBy(d => d.Amount.Amount)!;
        var summary = best.Amount.Amount > 0m
            ? localizer.Format("Reports.Chart.Best", best.Day.ToDayHeading(culture), best.Amount.ToDisplayString(culture))
            : localizer["Reports.Chart.NoBest"];

        return new TakingsChart(
            $"0 0 {Number(Width)} {Number(Height)}",
            Number(Width),
            Number(layout.Baseline),
            bars,
            days[0].Day.ToString(EdgePattern, culture),
            days[^1].Day.ToString(EdgePattern, culture),
            summary);
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}