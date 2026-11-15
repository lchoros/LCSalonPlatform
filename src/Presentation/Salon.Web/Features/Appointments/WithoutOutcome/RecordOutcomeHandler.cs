using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Services.Appointments;
using Salon.Services.Settings;

namespace Salon.Web.Features.Appointments.WithoutOutcome;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): "came" for a booked visit is two moves, and both, their history
/// rows and their events commit together or not at all. The outcome service decides whether the visit still needs an
/// outcome and walks the state machine (ADR 0012).
/// </summary>
public sealed class RecordOutcomeHandler(IVisitOutcomeService outcomes, ISettingService settings) : IRequestHandler<RecordOutcomeCommand, RecordOutcomeResult>
{
    public async Task<RecordOutcomeResult> HandleAsync(RecordOutcomeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var visit = await outcomes.RecordAsync(request.AppointmentId, request.Outcome, request.ByUserId, cancellationToken);
        if (visit is null)
        {
            return new RecordOutcomeResult(false);
        }

        // A missed visit may carry a fee (ADR 0022): the controller then shows its preview, where a person decides.
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
        var proposal = CancellationPolicy.Propose(visit.Status, visit.Start, visit.CancelledAt, visit.Price, policy);
        return new RecordOutcomeResult(true, proposal is not null);
    }
}