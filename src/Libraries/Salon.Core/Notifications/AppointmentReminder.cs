using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Notifications;

/// <summary>
/// The record that one reminder was handled for one appointment at one start time (ADR 0020). The row is written before
/// the SMS is handed over and is unique per appointment, kind and start, so a retry or two workers can never text a
/// client twice; a moved appointment has a new start and is reminded again.
/// </summary>
public class AppointmentReminder : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid AppointmentId { get; set; }

    public ReminderKind Kind { get; set; }

    /// <summary>The start the reminder was about; part of the uniqueness.</summary>
    public Instant AppointmentStart { get; set; }

    public ReminderOutcome Outcome { get; set; }

    public Instant CreatedAt { get; set; }
}

public enum ReminderKind
{
    First = 1,

    Second = 2,
}

public enum ReminderOutcome
{
    /// <summary>Claimed; the message is being handed to the senders.</summary>
    Pending = 0,

    Sent = 1,

    /// <summary>The provider threw; not retried (a late duplicate is worse than a missed reminder).</summary>
    Failed = 2,

    /// <summary>
    /// Nobody to tell: the card has no phone number and no email address the salon sends to (ADR 0025). The name is
    /// from when reminders were SMS only; the stored value stays.
    /// </summary>
    NoPhone = 3,

    /// <summary>Another reminder for the same visit went out in the same pass; this one is recorded so it never follows.</summary>
    Superseded = 4,
}