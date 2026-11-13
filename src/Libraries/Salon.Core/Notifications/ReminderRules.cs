using NodaTime;

using Salon.Core.Settings;

namespace Salon.Core.Notifications;

/// <summary>
/// When a reminder goes out (ADR 0020). Pure: the lead time before the visit, pushed to the end of quiet hours when it
/// would land inside them, and dropped when that leaves the client less than <see cref="MinimumNotice"/>.
/// </summary>
public static class ReminderRules
{
    /// <summary>A reminder closer to the visit than this is noise, not help.</summary>
    public static readonly Duration MinimumNotice = Duration.FromMinutes(30);

    /// <summary>The reminders a salon has switched on, with their lead times.</summary>
    public static IReadOnlyList<(ReminderKind Kind, Duration Lead)> Plan(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.RemindersEnabled)
        {
            return [];
        }

        var plan = new List<(ReminderKind, Duration)>(2);
        if (settings.FirstReminderHours > 0)
        {
            plan.Add((ReminderKind.First, Duration.FromHours(settings.FirstReminderHours)));
        }

        // Two reminders at the same moment would be one message sent twice.
        if (settings.SecondReminderHours > 0 && settings.SecondReminderHours != settings.FirstReminderHours)
        {
            plan.Add((ReminderKind.Second, Duration.FromHours(settings.SecondReminderHours)));
        }

        return plan;
    }

    /// <summary>
    /// The instant to send at, or null when no acceptable moment exists: <paramref name="start"/> minus
    /// <paramref name="lead"/>, moved to the end of quiet hours if it falls inside them, never later than
    /// <see cref="MinimumNotice"/> before the visit.
    /// </summary>
    public static Instant? SendAt(Instant start, Duration lead, int quietStartMinute, int quietEndMinute, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (lead <= Duration.Zero)
        {
            return null;
        }

        var sendAt = start - lead;
        if (IsQuiet(sendAt, quietStartMinute, quietEndMinute, zone))
        {
            sendAt = QuietEnd(sendAt, quietEndMinute, zone);
        }

        return sendAt <= start - MinimumNotice ? sendAt : null;
    }

    /// <summary>
    /// True when the reminder should go out now: its moment has come, the visit is still far enough away, and the visit
    /// was not booked after that moment (the booking confirmation already said everything).
    /// </summary>
    public static bool IsDue(Instant? sendAt, Instant now, Instant start, Instant bookedAt)
        => sendAt is { } at && at <= now && now <= start - MinimumNotice && bookedAt < at;

    /// <summary>True when <paramref name="instant"/> is inside quiet hours in <paramref name="zone"/>; the window may wrap midnight.</summary>
    public static bool IsQuiet(Instant instant, int quietStartMinute, int quietEndMinute, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var from = Clamp(quietStartMinute);
        var to = Clamp(quietEndMinute);
        if (from == to)
        {
            return false;
        }

        var local = instant.InZone(zone).TimeOfDay;
        var minute = (local.Hour * 60) + local.Minute;
        return from < to ? minute >= from && minute < to : minute >= from || minute < to;
    }

    /// <summary>The first instant after <paramref name="instant"/> where the local clock shows the end of quiet hours.</summary>
    private static Instant QuietEnd(Instant instant, int quietEndMinute, DateTimeZone zone)
    {
        var local = instant.InZone(zone).LocalDateTime;
        var end = local.Date.At(LocalTime.FromMinutesSinceMidnight(Clamp(quietEndMinute)));
        if (end <= local)
        {
            end = end.PlusDays(1);
        }

        // Lenient: an end that falls in a spring-forward gap moves to the first valid time after it.
        return zone.AtLeniently(end).ToInstant();
    }

    private static int Clamp(int minuteOfDay) => Math.Clamp(minuteOfDay, 0, NotificationSettings.MinutesPerDay - 1);
}