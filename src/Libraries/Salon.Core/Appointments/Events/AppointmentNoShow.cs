using NodaTime;
using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>Raised when the client did not turn up (ADR 0012); the no-show policy (M2) listens here.</summary>
public sealed record AppointmentNoShow(Guid AppointmentId, Guid TenantId, Instant At) : IEvent;