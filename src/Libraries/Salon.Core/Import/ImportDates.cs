using System.Globalization;
using NodaTime;

namespace Salon.Core.Import;

/// <summary>
/// Reads dates, times and lengths of time as a person or an old system wrote them into a diary export (ADR 0067).
/// Pure. Only the day-first and ISO orders are read: a file written month-first is recognised as such
/// (<see cref="LooksMonthFirst"/>) so the whole file can be refused instead of every visit landing on the wrong day.
/// </summary>
public static class ImportDates
{
    private static readonly char[] DateSeparators = ['.', '/', '-'];

    /// <summary>
    /// True with null for a blank cell, true with the day for a date, false for anything else. ISO
    /// (<c>2026-11-20</c>) and day-first (<c>20.11.2026</c>, <c>20/11/2026</c>, <c>20-11-2026</c>) with a four-digit year.
    /// </summary>
    public static bool TryDate(string? text, out LocalDate? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (Parts(text.Trim()) is not { } parts)
        {
            return false;
        }

        var (year, month, day) = parts.YearFirst ? (parts.A, parts.B, parts.C) : (parts.C, parts.B, parts.A);
        if (year < 1900 || year > 9999 || month is < 1 or > 12 || day < 1 || day > CalendarSystem.Iso.GetDaysInMonth(year, month))
        {
            return false;
        }

        date = new LocalDate(year, month, day);
        return true;
    }

    /// <summary>
    /// Whether a date cell can only be read month-first: written day-first style, its first number is a possible
    /// month and its second is not (<c>11/20/2026</c>). One such cell in a file means the file is month-first.
    /// </summary>
    public static bool LooksMonthFirst(string? text)
        => !string.IsNullOrWhiteSpace(text) && Parts(text.Trim()) is { YearFirst: false } parts && parts.A is >= 1 and <= 12 && parts.B > 12;

    /// <summary>
    /// True with null for a blank cell, true with the time for a time of day, false for anything else:
    /// <c>14:30</c>, <c>9:05</c>, <c>14:30:00</c> (seconds only as nought), <c>14.30</c>, <c>2:30 PM</c>, <c>2.30pm</c>.
    /// </summary>
    public static bool TryTime(string? text, out LocalTime? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var value = text.Trim().ToUpperInvariant();
        int? half = null;
        if (value.EndsWith("AM", StringComparison.Ordinal) || value.EndsWith("PM", StringComparison.Ordinal))
        {
            half = value.EndsWith("PM", StringComparison.Ordinal) ? 12 : 0;
            value = value[..^2].TrimEnd();
        }

        var pieces = value.Split(':', '.');
        if (pieces.Length is < 2 or > 3 || pieces.Any(p => p.Length is 0 or > 2 || !p.All(char.IsAsciiDigit)) || pieces[1].Length != 2)
        {
            return false;
        }

        var hour = int.Parse(pieces[0], CultureInfo.InvariantCulture);
        var minute = int.Parse(pieces[1], CultureInfo.InvariantCulture);
        var second = pieces.Length == 3 ? int.Parse(pieces[2], CultureInfo.InvariantCulture) : 0;
        if (half is { } offset)
        {
            // 12:00 AM is midnight and 12:00 PM is noon; a 24-hour time with AM or PM is not a time.
            if (hour is < 1 or > 12)
            {
                return false;
            }

            hour = (hour % 12) + offset;
        }

        // A diary is kept to the minute: a time with seconds that are not nought is not a diary time.
        if (hour > 23 || minute > 59 || second != 0)
        {
            return false;
        }

        time = new LocalTime(hour, minute);
        return true;
    }

    /// <summary>
    /// True with null for a blank cell, true with the moment for a date and a time in one cell (<c>2026-11-20 14:30</c>,
    /// <c>20.11.2026 14:30</c>, <c>2026-11-20T14:30:00</c>), false for anything else, a date without a time included.
    /// </summary>
    public static bool TryDateTime(string? text, out LocalDateTime? moment)
    {
        moment = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var value = text.Trim();
        var cut = value.IndexOfAny([' ', 'T']);
        if (cut <= 0)
        {
            return false;
        }

        if (!TryDate(value[..cut], out var date) || !TryTime(value[(cut + 1)..], out var time) || date is not { } day || time is not { } clock)
        {
            return false;
        }

        moment = day + clock;
        return true;
    }

    /// <summary>The date part of a date-time cell, for <see cref="LooksMonthFirst"/>; the cell itself when it has no time.</summary>
    public static string? DatePart(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        var cut = value.IndexOfAny([' ', 'T']);
        return cut > 0 ? value[..cut] : value;
    }

    /// <summary>
    /// True with null for a blank cell, true with the minutes for a length of time, false for anything else: whole
    /// minutes (<c>45</c>, <c>45 min</c>) or hours and minutes (<c>1:30</c>, <c>1h 30</c>, <c>1h30m</c>, <c>2h</c>).
    /// </summary>
    public static bool TryMinutes(string? text, out int? minutes)
    {
        minutes = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var value = text.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (value.Contains(':', StringComparison.Ordinal))
        {
            var pieces = value.Split(':');
            if (pieces.Length != 2 || pieces[1].Length != 2 || !pieces.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            {
                return false;
            }

            var hours = int.Parse(pieces[0], CultureInfo.InvariantCulture);
            var rest = int.Parse(pieces[1], CultureInfo.InvariantCulture);
            if (rest > 59 || hours > 24)
            {
                return false;
            }

            minutes = (hours * 60) + rest;
            return true;
        }

        var h = value.IndexOf('h', StringComparison.Ordinal);
        if (h > 0)
        {
            var hourText = value[..h];
            var minuteText = value[(h + 1)..].TrimEnd('m', 'i', 'n', 's');
            if (!hourText.All(char.IsAsciiDigit) || !minuteText.All(char.IsAsciiDigit) || hourText.Length > 2 || minuteText.Length > 2)
            {
                return false;
            }

            var hours = int.Parse(hourText, CultureInfo.InvariantCulture);
            var rest = minuteText.Length == 0 ? 0 : int.Parse(minuteText, CultureInfo.InvariantCulture);
            if (rest > 59 || hours > 24)
            {
                return false;
            }

            minutes = (hours * 60) + rest;
            return true;
        }

        if (!ImportNumbers.TryWhole(text, out var whole) || whole is not { } count)
        {
            return false;
        }

        minutes = count;
        return true;
    }

    /// <summary>
    /// A local date and time in the salon's zone as one moment. Null when the clocks skip that time (spring) or pass
    /// it twice (autumn): an import does not guess which was meant.
    /// </summary>
    public static Instant? InZone(LocalDateTime local, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var mapping = zone.MapLocal(local);
        return mapping.Count == 1 ? mapping.Single().ToInstant() : null;
    }

    /// <summary>Whether a local time in the zone is skipped (0) or passed twice (2); 1 for an ordinary time.</summary>
    public static int Mappings(LocalDateTime local, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.MapLocal(local).Count;
    }

    private static DateParts? Parts(string text)
    {
        var separator = text.IndexOfAny(DateSeparators);
        if (separator < 0)
        {
            return null;
        }

        var pieces = text.Split(text[separator]);
        if (pieces.Length != 3 || pieces.Any(p => p.Length is 0 or > 4 || !p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        var yearFirst = pieces[0].Length == 4;
        if (yearFirst ? pieces[1].Length > 2 || pieces[2].Length > 2 : pieces[0].Length > 2 || pieces[1].Length > 2 || pieces[2].Length != 4)
        {
            return null;
        }

        return new DateParts(int.Parse(pieces[0], CultureInfo.InvariantCulture), int.Parse(pieces[1], CultureInfo.InvariantCulture), int.Parse(pieces[2], CultureInfo.InvariantCulture), yearFirst);
    }

    /// <summary>The three numbers of a date in the order written.</summary>
    private sealed record DateParts(int A, int B, int C, bool YearFirst);
}