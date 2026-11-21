using System.Globalization;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Where a visit's block sits in the calendar's grid (ADR 0016, ADR 0043): the arithmetic the view used to do itself.
/// Pure, so it can be tested without rendering a page.
/// </summary>
public static class CalendarBlockStyle
{
    /// <summary>
    /// Row placement and lane offset. The look is the stylesheet's: a pill in a depth of the brand chosen by the visit's
    /// status (ADR 0086, ADR 0092); the person's own colour is the dot in the column's heading, not an edge on every visit.
    /// </summary>
    public static string For(CalendarBlock block)
    {
        ArgumentNullException.ThrowIfNull(block);
        return $"grid-row: {block.Row} / span {block.Span}; grid-column: 1; margin-left: {Percent(block.Lane, block.Lanes)}%; width: {Percent(1, block.Lanes)}%";
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