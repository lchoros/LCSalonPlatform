using NodaTime;

using Salon.Core.Events;

namespace Salon.Core.Appointments.Events;

/// <summary>Raised when an appointment is booked (ADR 0012). Ids and instants only; consumers load names and phones themselves.</summary>
public sealed record AppointmentBooked(
    Guid AppointmentId,
    Guid TenantId,
    Guid ClientId,
    Guid StaffMemberId,
    Guid ServiceId,
    Instant Start,
    Instant End,
    AppointmentSource Source) : IEvent;