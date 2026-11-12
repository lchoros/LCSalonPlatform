using NodaTime;

using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>
/// Raised when an appointment is cancelled by either side (ADR 0012). <see cref="Status"/> is
/// <see cref="AppointmentStatus.CancelledByClient"/> or <see cref="AppointmentStatus.CancelledBySalon"/>; the reason stays
/// in the status history, not in the event.
/// </summary>
public sealed record AppointmentCancelled(Guid AppointmentId, Guid TenantId, Instant At, AppointmentStatus Status) : IEvent;