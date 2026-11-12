using NodaTime;

using Salon.Core.Appointments;

namespace Salon.Services.Appointments;

/// <summary>
/// Books appointments and moves them through their life cycle (ADR 0012). Every change goes through
/// <see cref="AppointmentStateMachine"/>, writes a status-history row and publishes an event through the outbox. Called
/// from a command, all of it shares the pipeline's transaction (ADR 0003).
/// </summary>
public interface IBookingService
{
    /// <summary>
    /// Re-checks that <see cref="BookAppointmentRequest.Start"/> is a bookable slot for that service and staff member on
    /// that day, snapshots price and duration, inserts the appointment and its first status change and publishes
    /// <see cref="Core.Appointments.Events.AppointmentBooked"/>. Throws <see cref="SlotNoLongerAvailableException"/> when the
    /// slot is not offered any more or Postgres refuses the overlap (another booking won the race).
    /// </summary>
    Task<Appointment> BookAsync(BookAppointmentRequest request, CancellationToken cancellationToken = default);

    Task<Appointment> ConfirmAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default);

    Task<Appointment> MarkArrivedAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default);

    Task<Appointment> CompleteAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default);

    Task<Appointment> MarkNoShowAsync(Guid appointmentId, Guid? byUserId = null, CancellationToken cancellationToken = default);

    /// <summary><paramref name="byClient"/> picks <see cref="AppointmentStatus.CancelledByClient"/> or <see cref="AppointmentStatus.CancelledBySalon"/>.</summary>
    Task<Appointment> CancelAsync(Guid appointmentId, bool byClient, string? reason, Guid? byUserId = null, CancellationToken cancellationToken = default);

    /// <summary>The active (booked, confirmed, arrived) appointments starting on <paramref name="day"/> in the salon's zone, ordered by start.</summary>
    Task<IReadOnlyList<Appointment>> GetDayAsync(LocalDate day, CancellationToken cancellationToken = default);
}

/// <summary>What a booking needs; the client is referenced by id only (ADR 0011 owns the client record).</summary>
public sealed record BookAppointmentRequest(
    Guid ClientId,
    Guid StaffMemberId,
    Guid ServiceId,
    Instant Start,
    AppointmentSource Source,
    string? Notes = null,
    Guid? ByUserId = null);
