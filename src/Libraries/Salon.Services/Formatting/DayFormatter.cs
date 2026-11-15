using System.Globalization;
using NodaTime;

namespace Salon.Services.Formatting;

/// <summary>
/// A day the way the desk reads it off the diary: the weekday first, then the date in full ("Monday 16 November 2026").
/// The culture's own long date leaves the weekday out in en-GB, and the weekday is what a salon plans by.
/// </summary>
public static class DayFormatter
{
    private const string Pattern = "dddd d MMMM yyyy";

    public static string ToDayHeading(this LocalDate day, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return day.ToString(Pattern, culture);
    }
}