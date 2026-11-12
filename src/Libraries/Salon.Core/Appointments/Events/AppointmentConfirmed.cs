using NodaTime;

using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>Raised when a booked appointment is confirmed (ADR 0012).</summary>
public sealed record AppointmentConfirmed(Guid AppointmentId, Guid TenantId, Instant At) : IEvent;