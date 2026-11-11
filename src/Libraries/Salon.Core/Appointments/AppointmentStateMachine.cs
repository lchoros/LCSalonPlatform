using NodaTime;

namespace Salon.Core.Appointments;

/// <summary>
/// The only way an appointment changes status (ADR 0012). Pure: no clock, no database.
/// <c>Booked → Confirmed → Arrived → Completed</c>; <c>Booked|Confirmed → NoShow</c>;
/// <c>Booked|Confirmed|Arrived → CancelledByClient|CancelledBySalon</c>; Completed, NoShow and both cancelled states
/// are terminal. <see cref="Active"/> are the statuses that hold the staff member's time (and that the exclusion
/// constraint guards).
/// </summary>
public static class AppointmentStateMachine
{
    private static readonly AppointmentStatus[] Cancelled = [AppointmentStatus.CancelledByClient, AppointmentStatus.CancelledBySalon];

    private static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> Allowed =
        new()
        {
            [AppointmentStatus.Booked] = [AppointmentStatus.Confirmed, AppointmentStatus.NoShow, .. Cancelled],
            [AppointmentStatus.Confirmed] = [AppointmentStatus.Arrived, AppointmentStatus.NoShow, .. Cancelled],
            [AppointmentStatus.Arrived] = [AppointmentStatus.Completed, .. Cancelled],
            [AppointmentStatus.Completed] = [],
            [AppointmentStatus.NoShow] = [],
            [AppointmentStatus.CancelledByClient] = [],
            [AppointmentStatus.CancelledBySalon] = [],
        };

    /// <summary>Booked, Confirmed and Arrived: the appointment still occupies its slot.</summary>
    public static IReadOnlyList<AppointmentStatus> Active { get; } =
        [AppointmentStatus.Booked, AppointmentStatus.Confirmed, AppointmentStatus.Arrived];

    public static bool IsActive(AppointmentStatus status) => Active.Contains(status);

    public static bool IsCancelled(AppointmentStatus status) => Cancelled.Contains(status);

    public static bool IsTerminal(AppointmentStatus status) => Allowed.TryGetValue(status, out var next) && next.Length == 0;

    public static bool CanTransition(AppointmentStatus from, AppointmentStatus to)
        => Allowed.TryGetValue(from, out var next) && next.Contains(to);

    /// <summary>
    /// Moves <paramref name="appointment"/> to <paramref name="to"/> and returns the history row to persist. A cancellation
    /// also stamps <see cref="Appointment.CancelledAt"/> and <see cref="Appointment.CancellationReason"/>. Throws
    /// <see cref="InvalidAppointmentTransitionException"/> and changes nothing when the move is not allowed.
    /// </summary>
    public static AppointmentStatusChange Transition(Appointment appointment, AppointmentStatus to, Instant now, Guid? byUserId, string? reason)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        var from = appointment.Status;
        if (!CanTransition(from, to))
        {
            throw new InvalidAppointmentTransitionException(from, to);
        }

        appointment.Status = to;
        if (IsCancelled(to))
        {
            appointment.CancelledAt = now;
            appointment.CancellationReason = reason;
        }

        return new AppointmentStatusChange
        {
            TenantId = appointment.TenantId,
            AppointmentId = appointment.Id,
            From = from,
            To = to,
            At = now,
            ByUserId = byUserId,
            Reason = reason,
        };
    }
}
