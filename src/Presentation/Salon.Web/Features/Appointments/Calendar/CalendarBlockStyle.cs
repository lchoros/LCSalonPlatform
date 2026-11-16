using System.Globalization;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Where a visit's block sits in the calendar's grid (ADR 0016, ADR 0043): the arithmetic the view used to do itself.
/// Pure, so it can be tested without rendering a page.
/// </summary>
public static class CalendarBlockStyle
{
    /// <summary>Row placement, lane offset and the staff colour as a light fill with a solid edge.</summary>
    public static string For(CalendarBlock block, string colour)
    {
        ArgumentNullException.ThrowIfNull(block);
        return $"grid-row: {block.Row} / span {block.Span}; grid-column: 1; margin-left: {Percent(block.Lane, block.Lanes)}%; width: {Percent(1, block.Lanes)}%; background-color: {colour}26; border-color: {colour}";
    }

    /// <summary>A share as a CSS percentage: a point as the decimal sign whatever the reader's language.</summary>
    public static string Percent(int part, int whole) => (part * 100m / whole).ToString("0.##", CultureInfo.InvariantCulture);
}