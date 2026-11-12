using NodaTime;

namespace Salon.Services.Availability;

/// <summary>A bookable start for one staff member (ADR 0012). <see cref="End"/> excludes the buffer; the buffer is only kept free.</summary>
public sealed record Slot(Guid StaffMemberId, Instant Start, Instant End);