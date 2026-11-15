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

    /// <summary>
    /// A moment as the salon saw it on its own clock, short date and time ("16/11/2026 14:05" in en-GB): for a line of
    /// history that says when something was recorded. One place, so every page writes it the same way.
    /// </summary>
    public static string ToShortDateTime(this Instant at, DateTimeZone zone, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(culture);

        return at.InZone(zone).LocalDateTime.ToString("g", culture);
    }
}