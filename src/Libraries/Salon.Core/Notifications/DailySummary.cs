using System.Globalization;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Settings;
using Salon.Core.Tenancy;

namespace Salon.Core.Notifications;

/// <summary>
/// The record that the summary of one day went out for one salon (ADR 0050). The row is written before the email is
/// handed over and is unique per salon and day, so a retry or two workers can never send a day twice.
/// </summary>
public class DailySummaryDelivery : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>The day the summary is of, on the salon's calendar.</summary>
    public LocalDate Day { get; set; }

    public Instant SentAt { get; set; }

    /// <summary>How many owners it was handed over for; 0 when the salon has no owner to write to or every hand-over failed.</summary>
    public int Recipients { get; set; }
}

/// <summary>When a salon's daily summary goes out (ADR 0050). Pure.</summary>
public static class DailySummaryRules
{
    /// <summary>
    /// Due from the chosen minute until midnight on the salon's clock, when switched on. After midnight the day is
    /// over: a summary that was missed is not sent late, the report has the numbers.
    /// </summary>
    public static bool IsDue(DailySummarySettings settings, LocalDateTime salonNow)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Enabled && IsTimeValid(settings.SendAtMinute) && MinuteOfDay(salonNow.TimeOfDay) >= settings.SendAtMinute;
    }

    public static bool IsTimeValid(int minuteOfDay) => minuteOfDay is >= 0 and < DailySummarySettings.MinutesPerDay;

    public static int MinuteOfDay(LocalTime time) => (time.Hour * 60) + time.Minute;

    /// <summary>The minute of the day as a form writes it, "20:00".</summary>
    public static string ToClock(int minuteOfDay)
        => IsTimeValid(minuteOfDay)
            ? string.Create(CultureInfo.InvariantCulture, $"{minuteOfDay / 60:00}:{minuteOfDay % 60:00}")
            : string.Empty;

    /// <summary>Reads "20:00" or "8:30" as a minute of the day; null for anything else.</summary>
    public static int? ParseClock(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Trim().Split(':');
        return parts.Length == 2
            && parts[0].Length is 1 or 2 && parts[1].Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hour)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minute)
            && hour is >= 0 and < 24 && minute is >= 0 and < 60
            ? (hour * 60) + minute
            : null;
    }
}