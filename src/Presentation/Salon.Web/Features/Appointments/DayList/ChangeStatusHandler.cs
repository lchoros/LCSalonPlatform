using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.DayList;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the status, its history row and the outbox event commit together.
/// The state machine is consulted first so a refused move returns a result instead of rolling back an exception; the
/// booking service then performs the move and publishes the event (ADR 0012).
/// </summary>
public sealed class ChangeStatusHandler(
    SalonDbContext db,
    IBookingService booking,
    ISettingService settings,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<ChangeStatusCommand, ChangeStatusResult>
{
    public async Task<ChangeStatusResult> HandleAsync(ChangeStatusCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ChangeStatusCommand)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];

        var current = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == request.AppointmentId)
            .Select(a => new { a.Start, a.Status })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new AppointmentNotFoundException(request.AppointmentId);
        var day = current.Start.InZone(zone).Date;
        var target = AppointmentTransitions.Target(request.Transition);
        if (!AppointmentStateMachine.CanTransition(current.Status, target))
        {
            return new ChangeStatusResult(day, false, current.Status, target);
        }

        var moved = request.Transition switch
        {
            AppointmentTransition.Confirm => await booking.ConfirmAsync(request.AppointmentId, request.ByUserId, cancellationToken),
            AppointmentTransition.Arrived => await booking.MarkArrivedAsync(request.AppointmentId, request.ByUserId, cancellationToken),
            AppointmentTransition.Complete => await booking.CompleteAsync(request.AppointmentId, request.ByUserId, cancellationToken),
            AppointmentTransition.NoShow => await booking.MarkNoShowAsync(request.AppointmentId, request.ByUserId, cancellationToken),
            AppointmentTransition.Cancel => await booking.CancelAsync(request.AppointmentId, byClient: false, null, request.ByUserId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Transition, "Unknown transition."),
        };

        // A missed or late-cancelled visit may carry a fee (ADR 0022): the controller then shows its preview instead of the day.
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
        var proposal = CancellationPolicy.Propose(moved.Status, moved.Start, moved.CancelledAt, moved.Price, policy);
        return new ChangeStatusResult(day, true, current.Status, moved.Status, proposal is not null);
    }
}