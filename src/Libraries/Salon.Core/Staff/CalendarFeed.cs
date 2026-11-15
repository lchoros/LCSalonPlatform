using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Core.Staff;

/// <summary>
/// The secret in a staff member's calendar address (ADR 0038): 32 random bytes as base64url (43 characters). A calendar
/// app cannot sign in, so knowing the address is the only credential needed to read that person's visits. It is never
/// logged by the application, it can be replaced at any time, and replacing it switches the old address off.
/// </summary>
public static class CalendarFeedToken
{
    public const int Bytes = 32;

    /// <summary>Length of the base64url text of <see cref="Bytes"/> bytes.</summary>
    public const int Length = 43;

    public static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    /// <summary>True for text of the right length and alphabet; a cheap guard before the database is asked.</summary>
    public static bool LooksValid(string? token)
        => token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>What a staff member's calendar address shows (ADR 0038). Pure.</summary>
public static class CalendarFeedRules
{
    /// <summary>How far back the feed reaches: a calendar app keeps what it once saw only while the feed still lists it.</summary>
    public const int PastDays = 30;

    /// <summary>How far ahead: further than any salon books.</summary>
    public const int FutureDays = 365;

    /// <summary>A visit that holds its time or took place is in the diary; a cancelled or missed one is not.</summary>
    public static bool Shows(AppointmentStatus status) => ShownStatuses.Contains(status);

    /// <summary>The statuses <see cref="Shows"/> accepts, for a query that filters in the database.</summary>
    public static IReadOnlyList<AppointmentStatus> ShownStatuses { get; } =
        [AppointmentStatus.Booked, AppointmentStatus.Confirmed, AppointmentStatus.Arrived, AppointmentStatus.Completed];

    public static Instant From(Instant now) => now - Duration.FromDays(PastDays);

    public static Instant Until(Instant now) => now + Duration.FromDays(FutureDays);

    /// <summary>
    /// The title of a visit in somebody's private calendar: the service, then the client's first name and the initial
    /// of the last name ("Cut &amp; blow-dry · Olivia B."). The feed leaves the salon for a calendar provider, so it
    /// carries enough to recognise the visit and nothing more: no full name, no phone number, no notes.
    /// </summary>
    public static string Summary(string serviceName, string clientFirstName, string clientLastName)
    {
        var first = clientFirstName?.Trim() ?? string.Empty;
        var last = clientLastName?.Trim() ?? string.Empty;
        var initial = last.Length > 0 ? $" {last[..1].ToUpperInvariant()}." : string.Empty;
        var who = (first + initial).Trim();
        return who.Length == 0 ? serviceName : $"{serviceName} · {who}";
    }
}

/// <summary>One visit as a calendar entry. An app that subscribes replaces its copy by <paramref name="Id"/> on every read, so a moved visit moves.</summary>
public sealed record CalendarFeedEvent(Guid Id, Instant Start, Instant End, string Summary, string? Location);

/// <summary>
/// Writes a calendar in the iCalendar format (RFC 5545) that Google, Apple and Outlook subscribe to. Pure. Times are
/// written in UTC so no time zone table is needed; text is escaped; lines end with CR LF and are folded at 75 octets,
/// as the format demands of a writer.
/// </summary>
public static class CalendarFeedWriter
{
    public const string ContentType = "text/calendar; charset=utf-8";

    private const int MaxLineOctets = 75;
    private const string TimeFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <param name="calendarName">What the calendar is called in the app that subscribes: the person and the salon.</param>
    /// <param name="host">The salon's host name; makes each entry's id unique in the world.</param>
    public static string Write(string calendarName, string host, IEnumerable<CalendarFeedEvent> events, Instant now)
    {
        ArgumentNullException.ThrowIfNull(calendarName);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(events);

        var text = new StringBuilder();
        Line(text, "BEGIN:VCALENDAR");
        Line(text, "VERSION:2.0");
        Line(text, "PRODID:-//Salon Platform//Calendar feed//EN");
        Line(text, "CALSCALE:GREGORIAN");
        Line(text, "METHOD:PUBLISH");
        Line(text, "X-WR-CALNAME:" + Escape(calendarName));
        foreach (var entry in events)
        {
            Line(text, "BEGIN:VEVENT");
            Line(text, $"UID:{entry.Id:N}@{host}");
            Line(text, "DTSTAMP:" + Time(now));
            Line(text, "DTSTART:" + Time(entry.Start));
            Line(text, "DTEND:" + Time(entry.End));
            Line(text, "SUMMARY:" + Escape(entry.Summary));
            if (!string.IsNullOrWhiteSpace(entry.Location))
            {
                Line(text, "LOCATION:" + Escape(entry.Location));
            }

            Line(text, "END:VEVENT");
        }

        Line(text, "END:VCALENDAR");
        return text.ToString();
    }

    /// <summary>Backslash, semicolon and comma are escaped; a line break becomes the two characters backslash and n.</summary>
    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(";", "\\;", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\n", StringComparison.Ordinal);
    }

    private static string Time(Instant instant) => instant.ToDateTimeUtc().ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Appends one content line, folded: no physical line is longer than 75 octets of UTF-8, a continuation starts with
    /// a space, and a character of several octets is never split.
    /// </summary>
    private static void Line(StringBuilder text, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > MaxLineOctets)
            {
                text.Append("\r\n ");
                octets = 1;
            }

            text.Append(rune.ToString());
            octets += size;
        }

        text.Append("\r\n");
    }
}