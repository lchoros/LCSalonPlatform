using NodaTime;

namespace Salon.Services.Availability;

/// <summary>
/// Loads what the engine needs for the current tenant (ADR 0012) — the salon zone, the service, the bookable active
/// staff with their rota, the active appointments in the window and <see cref="Core.Settings.BookingSettings"/> — and runs
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
}