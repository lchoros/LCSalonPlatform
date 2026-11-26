using System.Globalization;
using NodaTime;
using NodaTime.Text;

namespace Salon.Web.Framework.Api;

/// <summary>
/// How the booking API writes and reads times (ADR 0097): days "yyyy-MM-dd" and clock times "HH:mm" in the salon's
/// zone, instants ISO-8601 in UTC ("2026-11-26T09:45:00Z"), the pattern the booking pages put in their addresses.
/// </summary>
public static class ApiFormat
{
    private static readonly LocalTimePattern ClockPattern = LocalTimePattern.Create("HH:mm", CultureInfo.InvariantCulture);

    public static string FormatDate(LocalDate date) => LocalDatePattern.Iso.Format(date);

    public static string FormatTime(LocalTime time) => ClockPattern.Format(time);

    public static string FormatInstant(Instant instant) => InstantPattern.ExtendedIso.Format(instant);

    /// <summary>True for a blank value (<paramref name="date"/> null) or a day in the ISO form; false for anything else.</summary>
    public static bool TryParseDate(string? text, out LocalDate? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parsed = LocalDatePattern.Iso.Parse(text.Trim());
        date = parsed.Success ? parsed.Value : null;
        return parsed.Success;
    }

    /// <summary>True for a blank value (<paramref name="instant"/> null) or an ISO-8601 instant; false for anything else.</summary>
    public static bool TryParseInstant(string? text, out Instant? instant)
    {
        instant = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parsed = InstantPattern.ExtendedIso.Parse(text.Trim());
        instant = parsed.Success ? parsed.Value : null;
        return parsed.Success;
    }

    /// <summary>The day, or null for a blank or unreadable value (a validator has refused the latter already).</summary>
    public static LocalDate? ParseDate(string? text) => TryParseDate(text, out var date) ? date : null;

    /// <summary>The instant, or null for a blank or unreadable value (a validator has refused the latter already).</summary>
    public static Instant? ParseInstant(string? text) => TryParseInstant(text, out var instant) ? instant : null;

    /// <summary>"morning" before 12:00, "afternoon" from 12:00 to 16:00, "evening" after: the parts the time page groups by.</summary>
    public static string PartOfDay(LocalTime time) => time.Hour switch
    {
        < 12 => "morning",
        < 16 => "afternoon",
        _ => "evening",
    };
}