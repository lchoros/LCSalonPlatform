using System.Globalization;
using NodaTime;
using Salon.Core.Reports;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Reports;

namespace Salon.Web.Features.Reports.Overview;

/// <summary>
/// One bar of the takings chart as the view writes it (ADR 0048). The numbers are text with a point as the decimal
/// sign whatever the reader's language: they are SVG attributes, and "12,5" is not a number there.
/// </summary>
/// <param name="Day">ISO date of the day, or of the first day of the week, for tests and for whoever reads the markup.</param>
/// <param name="Title">"Saturday 14 November 2026: £320.00", shown when the bar is pointed at.</param>
public sealed record TakingsChartBar(string X, string Y, string Width, string Height, bool Below, string Day, string Title);

/// <param name="Title">"Takings each day", or "Takings each week" for a longer period (ADR 0071).</param>
/// <param name="Summary">One sentence that says what the picture shows, for a screen reader and under the chart.</param>
public sealed record TakingsChart(string Title, string ViewBox, string Width, string Baseline, IReadOnlyList<TakingsChartBar> Bars, string First, string Last, string Summary);

/// <summary>
/// The takings of each day, or of each week of a longer period, as bars (ADR 0048, ADR 0071, ADR 0043): the geometry
/// is <see cref="BarChart"/>'s, the words are written here, and the view only writes the shapes down. No script and
/// no style attribute.
/// </summary>
public static class TakingsCharts
{
    /// <summary>The chart's own units; the page stretches the picture to the width it has.</summary>
    public const int Width = 620;

    public const int Height = 160;

    private const string EdgePattern = "d MMM";

    private const string WeekEndPattern = "d MMM yyyy";

    /// <summary>Null when the period is not drawn: no money, or a single day.</summary>
    public static TakingsChart? For(TakingsReport takings, ILocalizationService localizer, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(takings);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);

        if (takings.Daily is { Count: > 0 } days)
        {
            var best = days.MaxBy(d => d.Amount.Amount)!;
            return Draw(
                localizer["Reports.Chart.Title"],
                [.. days.Select(d => new Point(d.Day, d.Amount.Amount, localizer.Format("Reports.Chart.Bar", d.Day.ToDayHeading(culture), d.Amount.ToDisplayString(culture))))],
                best.Amount.Amount > 0m
                    ? localizer.Format("Reports.Chart.Best", best.Day.ToDayHeading(culture), best.Amount.ToDisplayString(culture))
                    : localizer["Reports.Chart.NoBest"],
                days[0].Day.ToString(EdgePattern, culture),
                days[^1].Day.ToString(EdgePattern, culture));
        }

        if (takings.Weekly is { Count: > 0 } weeks)
        {
            string Span(WeekTakings week) => localizer.Format("Reports.Chart.Week", week.From.ToString(EdgePattern, culture), week.To.ToString(WeekEndPattern, culture));
            var best = weeks.MaxBy(w => w.Amount.Amount)!;
            return Draw(
                localizer["Reports.Chart.TitleWeekly"],
                [.. weeks.Select(w => new Point(w.From, w.Amount.Amount, localizer.Format("Reports.Chart.Bar", Span(w), w.Amount.ToDisplayString(culture))))],
                best.Amount.Amount > 0m
                    ? localizer.Format("Reports.Chart.BestWeek", Span(best), best.Amount.ToDisplayString(culture))
                    : localizer["Reports.Chart.NoBestWeek"],
                weeks[0].From.ToString(EdgePattern, culture),
                weeks[^1].To.ToString(EdgePattern, culture));
        }

        return null;
    }

    private static TakingsChart Draw(string title, IReadOnlyList<Point> points, string summary, string first, string last)
    {
        var layout = BarChart.Layout([.. points.Select(p => p.Amount)], Width, Height);
        var bars = points
            .Zip(layout.Bars, (point, bar) => new TakingsChartBar(
                Number(bar.X),
                Number(bar.Y),
                Number(bar.Width),
                Number(bar.Height),
                bar.Below,
                point.Day.ToIso(),
                point.Title))
            .ToArray();
        return new TakingsChart(title, $"0 0 {Number(Width)} {Number(Height)}", Number(Width), Number(layout.Baseline), bars, first, last, summary);
    }

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>One bar before it is placed: the day it starts on, its money and what pointing at it says.</summary>
    private sealed record Point(LocalDate Day, decimal Amount, string Title);
}