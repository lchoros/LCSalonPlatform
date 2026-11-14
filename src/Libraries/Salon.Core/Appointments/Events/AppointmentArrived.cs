using NodaTime;
using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>Raised when the client is marked as arrived (ADR 0012).</summary>
public sealed record AppointmentArrived(Guid AppointmentId, Guid TenantId, Instant At) : IEvent;