using NodaTime;

namespace Salon.Services.Availability;

/// <summary>
/// Loads what the engine needs for the current tenant (ADR 0012) — the salon zone, the service, the bookable active
/// staff with their rota, the active appointments and time off in the window and <see cref="Core.Settings.BookingSettings"/> — and runs
/// <see cref="AvailabilityCalculator"/>. Everything goes through the tenant filter, so another salon's bookings never
/// shape the answer.
/// </summary>
public interface IAvailabilityService
{
    /// <summary>
    /// Bookable slots for <paramref name="serviceId"/> between the local dates <paramref name="fromDate"/> and <paramref name="toDate"/>
    /// (inclusive). With <paramref name="staffMemberId"/> the slots of that person; without, "any staff" (one slot per
    /// start). Throws <see cref="InvalidOperationException"/> when the service does not exist or is inactive.
    /// </summary>
    Task<IReadOnlyList<Slot>> GetSlotsAsync(Guid serviceId, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// As <see cref="GetSlotsAsync(Guid, Guid?, LocalDate, LocalDate, CancellationToken)"/>, but the appointment
    /// <paramref name="excludeAppointmentId"/> does not count as busy: the slots a visit could be moved to, including the
    /// time it holds now (ADR 0016).
    /// </summary>
    Task<IReadOnlyList<Slot>> GetSlotsAsync(Guid serviceId, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, Guid? excludeAppointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The free starts of a visit of several services that follow one another with one person (ADR 0095): only people
    /// who offer every one of them count, and the visit takes the sum of that person's own durations. A slot's
    /// <see cref="Slot.End"/> is the end of the last service. With one service this is
    /// <see cref="GetSlotsAsync(Guid, Guid?, LocalDate, LocalDate, CancellationToken)"/>.
    /// </summary>
    Task<IReadOnlyList<Slot>> GetVisitSlotsAsync(IReadOnlyList<Guid> serviceIds, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default);

    /// <summary>
    /// The free starts of a visit whose services may be done by different people (ADR 0096): each part names its
    /// service and the person, or nobody for "anyone who offers it". The parts follow one another without a gap. Where
    /// one person can do every part left open, that is preferred to a change of hands. A slot says who does each part.
    /// A named person who is not bookable or does not offer their part means there is no time at all.
    /// </summary>
    Task<IReadOnlyList<VisitSlot>> GetVisitPlanAsync(IReadOnlyList<VisitPart> parts, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default);
}

/// <summary>One service of a visit and who does it; no person means anyone who offers it (ADR 0096).</summary>
public sealed record VisitPart(Guid ServiceId, Guid? StaffMemberId);

/// <summary>A free start of a whole visit: from the first part's start to the last part's end, and who does each part, in order.</summary>
public sealed record VisitSlot(Instant Start, Instant End, IReadOnlyList<Guid> StaffMemberIds);