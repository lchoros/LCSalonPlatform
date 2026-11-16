using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace Salon.Services.Formatting;

/// <summary>
/// Times, machine-readable dates and small placeholders the way every page writes them. One place, so a view never
/// carries its own copy (ADR 0043): a view says what to show, these say how it is written.
/// </summary>
public static class ClockFormatter
{
    private const string EmDash = "—";

    /// <summary>A time of day as the reader's culture writes it: "14:05" in en-GB.</summary>
    public static string ToShortTime(this LocalTime time, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return time.ToString("t", culture);
    }

    /// <summary>A time of day as a rota states it, the same in every language: "09:30".</summary>
    public static string ToClockTime(this LocalTime time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>A day for an address or a <c>datetime</c> attribute: "2026-11-16".</summary>
    public static string ToIso(this LocalDate day) => LocalDatePattern.Iso.Format(day);

    /// <summary>A moment for an address or a <c>datetime</c> attribute: "2026-11-16T14:05:00Z".</summary>
    public static string ToIso(this Instant instant) => InstantPattern.ExtendedIso.Format(instant);

    /// <summary>Minutes as hours with at most one decimal, for "booked 31.5 of 40 working hours".</summary>
    public static string MinutesAsHours(int minutes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return (minutes / 60m).ToString("0.#", culture);
    }

    /// <summary>The text, or a dash where there is none, so an empty cell does not look like a mistake.</summary>
    public static string OrDash(this string? value) => string.IsNullOrEmpty(value) ? EmDash : value;
}