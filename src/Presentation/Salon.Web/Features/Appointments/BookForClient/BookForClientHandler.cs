using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the appointment, its first status change and the AppointmentBooked
/// outbox row commit together or not at all. The booking service re-checks the slot and lets
/// <see cref="SlotNoLongerAvailableException"/> through for the controller to turn into a message (ADR 0012).
/// </summary>
public sealed class BookForClientHandler(
    SalonDbContext db,
    IBookingService booking,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<BookForClientCommand, BookForClientResult>
{
    public async Task<BookForClientResult> HandleAsync(BookForClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has already refused blanks and a malformed start; the tenant guard, tenant-less requests.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(BookForClientCommand)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
        var start = InstantPattern.ExtendedIso.Parse(request.Start!.Trim()).GetValueOrThrow();

        var appointment = await booking.BookAsync(
            new BookAppointmentRequest(request.ClientId!.Value, request.StaffMemberId!.Value, request.ServiceId!.Value, start, AppointmentSource.Admin, null, request.ByUserId),
            cancellationToken);

        var clientName = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => c.FirstName + " " + c.LastName)
            .FirstAsync(cancellationToken);

        return new BookForClientResult(appointment.Id, clientName, appointment.Start.InZone(zone).LocalDateTime);
    }
}