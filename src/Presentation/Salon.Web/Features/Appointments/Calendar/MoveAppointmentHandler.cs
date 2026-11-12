using Microsoft.EntityFrameworkCore;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the new start, the "Moved" history row and the AppointmentMoved
/// outbox row commit together or not at all. The booking service's exceptions (not found, not active, slot taken) go
/// through for the controller to turn into a 404 or a message (ADR 0016).
/// </summary>
public sealed class MoveAppointmentHandler(
    SalonDbContext db,
    IBookingService booking,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<MoveAppointmentCommand, MoveAppointmentResult>
{
    public async Task<MoveAppointmentResult> HandleAsync(MoveAppointmentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has already refused a blank staff member and a malformed start; the tenant guard, tenant-less requests.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(MoveAppointmentCommand)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
        var start = InstantPattern.ExtendedIso.Parse(request.Start!.Trim()).GetValueOrThrow();

        var appointment = await booking.MoveAsync(request.AppointmentId, request.StaffMemberId!.Value, start, request.ByUserId, cancellationToken);

        var clientName = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => c.FirstName + " " + c.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == appointment.StaffMemberId)
            .Select(s => s.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new MoveAppointmentResult(appointment.Id, appointment.StaffMemberId, staffName, clientName, appointment.Start.InZone(zone).LocalDateTime);
    }
}