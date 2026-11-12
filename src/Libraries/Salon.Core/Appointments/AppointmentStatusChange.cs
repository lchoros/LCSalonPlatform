using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Appointments;

/// <summary>
/// One row per status transition of an appointment (ADR 0012), so the card and the no-show policy (M2) have a history.
/// The first row of an appointment has no <see cref="From"/>. Produced by <see cref="AppointmentStateMachine.Transition"/>.
/// </summary>
public class AppointmentStatusChange : BaseEntity, ITenantEntity
{
    public const int ReasonMaxLength = Appointment.CancellationReasonMaxLength;

    public Guid TenantId { get; set; }

    public Guid AppointmentId { get; set; }

    /// <summary>Null for the booking itself.</summary>
    public AppointmentStatus? From { get; set; }

    public AppointmentStatus To { get; set; }

    public Instant At { get; set; }

    /// <summary>The login that made the change; null when the client did it online or a job did.</summary>
    public Guid? ByUserId { get; set; }

    public string? Reason { get; set; }
}