using System.Globalization;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Where a visit's block sits in the calendar's grid (ADR 0016, ADR 0043): the arithmetic the view used to do itself.
/// Pure, so it can be tested without rendering a page.
/// </summary>
public static class CalendarBlockStyle
{
    /// <summary>
    /// Row placement, lane offset and the staff colour as the solid left edge; the fill is a shade of the brand chosen
    /// by the visit's status in the stylesheet (ADR 0086), so the calendar speaks one colour family.
    /// </summary>
    public static string For(CalendarBlock block, string colour)
    {
        ArgumentNullException.ThrowIfNull(block);
        return $"grid-row: {block.Row} / span {block.Span}; grid-column: 1; margin-left: {Percent(block.Lane, block.Lanes)}%; width: {Percent(1, block.Lanes)}%; border-color: {colour}";
    }

    /// <summary>Row placement and lane offset of a kept time; its dashed look comes from classes, not the staff colour.</summary>
    public static string For(CalendarKeptBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return $"grid-row: {block.Row} / span {block.Span}; grid-column: 1; margin-left: {Percent(block.Lane, block.Lanes)}%; width: {Percent(1, block.Lanes)}%";
    }

    /// <summary>A share as a CSS percentage: a point as the decimal sign whatever the reader's language.</summary>
    public static string Percent(int part, int whole) => (part * 100m / whole).ToString("0.##", CultureInfo.InvariantCulture);
}