using NodaTime;

using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>Raised when an appointment is completed (ADR 0012).</summary>
public sealed record AppointmentCompleted(Guid AppointmentId, Guid TenantId, Instant At) : IEvent;
