using NodaTime;

namespace Salon.Core.Appointments;

/// <summary>What the desk says about a visit whose time has passed (ADR 0034).</summary>
public enum VisitOutcome
{
    /// <summary>The client came and was served: the visit ends as completed.</summary>
    Came,

    /// <summary>The client did not turn up: the visit ends as a no-show.</summary>
    DidNotCome,
}

/// <summary>
/// Which visits still need an outcome and how one is recorded (ADR 0034). Pure: no clock, no database. A visit is
/// without an outcome when it still holds its time (booked, confirmed or arrived) although its day is over. Today's
/// visits are not counted: they are on the day list in front of the desk, and one that ended ten minutes ago is not
/// forgotten yet.
/// </summary>
public static class VisitOutcomeRules
{
    /// <summary>The first moment of today in the salon's zone; a visit that ended by then belongs to an earlier day.</summary>
    public static Instant Cutoff(Instant now, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return zone.AtStartOfDay(now.InZone(zone).Date).ToInstant();
    }

    public static bool IsMissing(AppointmentStatus status, Instant end, Instant cutoff)
        => AppointmentStateMachine.IsActive(status) && end <= cutoff;

    /// <summary>
    /// The outcomes the desk may choose for a visit in <paramref name="status"/>, in button order. Someone marked as
    /// arrived cannot be a no-show, so only "came" is left; a visit that already ended has none.
    /// </summary>
    public static IReadOnlyList<VisitOutcome> Choices(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Booked or AppointmentStatus.Confirmed => [VisitOutcome.Came, VisitOutcome.DidNotCome],
        AppointmentStatus.Arrived => [VisitOutcome.Came],
        _ => [],
    };

    /// <summary>
    /// The statuses to walk through, in order, to record <paramref name="outcome"/> from <paramref name="status"/>.
    /// The state machine has no shortcut from booked to completed, and none is added here: a client who came is
    /// first arrived and then completed, so the history reads as it would have on the day. Empty when the outcome is
    /// not one of the <see cref="Choices"/>.
    /// </summary>
    public static IReadOnlyList<AppointmentStatus> Steps(AppointmentStatus status, VisitOutcome outcome)
    {
        if (!Choices(status).Contains(outcome))
        {
            return [];
        }

        return outcome switch
        {
            VisitOutcome.DidNotCome => [AppointmentStatus.NoShow],
            VisitOutcome.Came when status == AppointmentStatus.Arrived => [AppointmentStatus.Completed],
            VisitOutcome.Came => [AppointmentStatus.Arrived, AppointmentStatus.Completed],
            _ => [],
        };
    }
}