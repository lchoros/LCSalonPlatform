using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Tenancy;

namespace Salon.Services.Appointments;

internal sealed class VisitOutcomeService(
    SalonDbContext db,
    IBookingService booking,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IVisitOutcomeService
{
    public async Task<int> CountMissingAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = await CutoffAsync(cancellationToken).ConfigureAwait(false);
        var active = AppointmentStateMachine.Active.ToArray();
        return await db.Appointments.CountAsync(a => active.Contains(a.Status) && a.End <= cutoff, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Appointment>> ListMissingAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = await CutoffAsync(cancellationToken).ConfigureAwait(false);
        var active = AppointmentStateMachine.Active.ToArray();
        return await db.Appointments.AsNoTracking()
            .Where(a => active.Contains(a.Status) && a.End <= cutoff)
            .OrderByDescending(a => a.Start).ThenBy(a => a.StaffMemberId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Appointment?> RecordAsync(Guid appointmentId, VisitOutcome outcome, Guid? byUserId = null, CancellationToken cancellationToken = default)
    {
        var cutoff = await CutoffAsync(cancellationToken).ConfigureAwait(false);
        var current = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.Status, a.End })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new AppointmentNotFoundException(appointmentId);
        if (!VisitOutcomeRules.IsMissing(current.Status, current.End, cutoff))
        {
            return null;
        }

        Appointment? visit = null;
        foreach (var step in VisitOutcomeRules.Steps(current.Status, outcome))
        {
            visit = step switch
            {
                AppointmentStatus.Arrived => await booking.MarkArrivedAsync(appointmentId, byUserId, cancellationToken).ConfigureAwait(false),
                AppointmentStatus.Completed => await booking.CompleteAsync(appointmentId, byUserId, cancellationToken).ConfigureAwait(false),
                AppointmentStatus.NoShow => await booking.MarkNoShowAsync(appointmentId, byUserId, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Recording a visit's outcome never moves it to {step}."),
            };
        }

        return visit;
    }

    private async Task<Instant> CutoffAsync(CancellationToken cancellationToken)
    {
        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        return VisitOutcomeRules.Cutoff(clock.GetCurrentInstant(), zone);
    }
}