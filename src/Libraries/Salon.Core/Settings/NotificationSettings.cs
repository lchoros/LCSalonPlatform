namespace Salon.Core.Settings;

/// <summary>
/// Appointment reminders per salon (ADR 0020). Lead times are whole hours before the visit; quiet hours are minutes of
/// the day in the salon's zone and may wrap midnight (21:00 → 08:00). A reminder that would fall in quiet hours waits
/// for them to end.
/// </summary>
public sealed class NotificationSettings : ISettings
{
    public const int MaxLeadHours = 168;

    public const int MinutesPerDay = 1440;

    public bool RemindersEnabled { get; set; } = true;

    /// <summary>
    /// Also email the client (ADR 0025): confirmations, reminders and the messages for a cancelled or moved visit go to
    /// the address on the card as well as by SMS. Off means SMS only.
    /// </summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>Hours before the visit for the first reminder; 0 switches it off.</summary>
    public int FirstReminderHours { get; set; } = 24;

    /// <summary>Hours before the visit for the second reminder; 0 switches it off.</summary>
    public int SecondReminderHours { get; set; } = 2;

    /// <summary>Minute of the day quiet hours begin (default 21:00).</summary>
    public int QuietHoursStartMinute { get; set; } = 21 * 60;

    /// <summary>Minute of the day quiet hours end (default 08:00). Equal to the start means no quiet hours.</summary>
    public int QuietHoursEndMinute { get; set; } = 8 * 60;
}