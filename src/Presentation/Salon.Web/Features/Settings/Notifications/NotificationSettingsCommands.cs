using System.Globalization;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Core.Staff;

namespace Salon.Web.Features.Settings.Notifications;

/// <summary>The salon's reminder settings as the form shows them (ADR 0020).</summary>
public sealed record GetNotificationSettingsQuery : IQuery<SaveNotificationSettingsCommand>;

/// <summary>
/// The reminder form as posted: hours as numbers, quiet hours as HH:mm text so that a browser without a time picker
/// still works. <see cref="SaveNotificationSettingsValidator"/> holds the rules.
/// </summary>
public sealed record SaveNotificationSettingsCommand(
    bool RemindersEnabled,
    int? FirstReminderHours,
    int? SecondReminderHours,
    string? QuietFrom,
    string? QuietTo) : ICommand<bool>
{
    public LocalTime? ParsedQuietFrom => WorkingHoursRules.TryParseTime(QuietFrom);

    public LocalTime? ParsedQuietTo => WorkingHoursRules.TryParseTime(QuietTo);

    public static SaveNotificationSettingsCommand From(NotificationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SaveNotificationSettingsCommand(
            settings.RemindersEnabled,
            settings.FirstReminderHours,
            settings.SecondReminderHours,
            Format(settings.QuietHoursStartMinute),
            Format(settings.QuietHoursEndMinute));
    }

    private static string Format(int minuteOfDay)
        => LocalTime.FromMinutesSinceMidnight(Math.Clamp(minuteOfDay, 0, NotificationSettings.MinutesPerDay - 1)).ToString("HH:mm", CultureInfo.InvariantCulture);
}