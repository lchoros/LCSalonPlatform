using Salon.Core.Appointments;

namespace Salon.Services.Appointments;

/// <summary>
/// Visits of the current salon whose day is over but that still hold their time, because nobody said whether the
/// client came (ADR 0034). The rule is <see cref="VisitOutcomeRules"/>; every change goes through
/// <see cref="IBookingService"/>, so the state machine, the history and the events are the ones of the day list.
/// </summary>
public interface IVisitOutcomeService
{
    /// <summary>How many visits are without an outcome; one cheap count, for the notice on the day list.</summary>
    Task<int> CountMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>The visits without an outcome, the latest first. Read-only entities.</summary>
    Task<IReadOnlyList<Appointment>> ListMissingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="outcome"/> for a visit without one and returns it as it is afterwards. Null, and nothing
    /// changes, when the visit already has an outcome, is of today or later, or cannot take this outcome (someone who
    /// arrived cannot be a no-show). An id the salon does not own throws <see cref="AppointmentNotFoundException"/>.
    /// Call inside a transaction: "came" for a booked visit is two steps.
    /// </summary>
    Task<Appointment?> RecordAsync(Guid appointmentId, VisitOutcome outcome, Guid? byUserId = null, CancellationToken cancellationToken = default);
}