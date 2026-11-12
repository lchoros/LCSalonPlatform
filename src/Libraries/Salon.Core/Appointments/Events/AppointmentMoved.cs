using NodaTime;

using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>
/// Raised when an active appointment is moved to another start and/or staff member from the admin calendar (ADR 0016).
/// Ids and instants only; <see cref="StaffMemberId"/> is the person after the move.
/// </summary>
public sealed record AppointmentMoved(Guid AppointmentId, Guid TenantId, Instant OldStart, Instant NewStart, Guid StaffMemberId) : IEvent;