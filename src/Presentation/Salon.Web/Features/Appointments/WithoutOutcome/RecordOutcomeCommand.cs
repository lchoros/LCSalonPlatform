using Salon.Core.Appointments;
using Salon.Core.Cqrs;

namespace Salon.Web.Features.Appointments.WithoutOutcome;

/// <summary>
/// Records what happened at one visit without an outcome (ADR 0034). The handler answers rather than throws when the
/// visit no longer needs one, so the controller can say so; an id the salon does not own surfaces as
/// <see cref="Salon.Services.Appointments.AppointmentNotFoundException"/>.
/// </summary>
/// <param name="ByUserId">The signed-in user (the <c>sub</c> claim), recorded on every history row.</param>
public sealed record RecordOutcomeCommand(Guid AppointmentId, VisitOutcome Outcome, Guid? ByUserId) : ICommand<RecordOutcomeResult>;

/// <param name="Recorded">False when the visit already had an outcome or cannot take this one; nothing changed then.</param>
/// <param name="FeeProposed">True when the salon's cancellation policy allows a fee for the visit as it is now (ADR 0022).</param>
public sealed record RecordOutcomeResult(bool Recorded, bool FeeProposed = false);

/// <summary>The URL segment of each outcome in <c>admin/appointments/without-outcome/{id}/{segment}</c>.</summary>
public static class VisitOutcomeSegments
{
    private static readonly Dictionary<string, VisitOutcome> BySegment = new(StringComparer.Ordinal)
    {
        ["came"] = VisitOutcome.Came,
        ["did-not-come"] = VisitOutcome.DidNotCome,
    };

    public static string Segment(VisitOutcome outcome) => BySegment.First(p => p.Value == outcome).Key;

    public static bool TryParse(string? segment, out VisitOutcome outcome)
        => BySegment.TryGetValue(segment ?? string.Empty, out outcome);
}