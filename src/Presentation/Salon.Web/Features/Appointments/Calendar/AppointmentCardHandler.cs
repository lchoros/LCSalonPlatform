using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Availability;
using Salon.Services.Payments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Reads through the tenant filter: the appointment, its client's name (never health notes, ADR 0011), service and staff,
/// then asks the availability engine for the free times of the chosen person and day with this appointment not counted
/// as busy (ADR 0016). A service made inactive meanwhile simply yields no times rather than failing the card.
/// </summary>
public sealed class AppointmentCardHandler(
    SalonDbContext db,
    IAvailabilityService availability,
    IClientAccountService accounts,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup) : IRequestHandler<AppointmentCardQuery, AppointmentCard>
{
    public async Task<AppointmentCard> HandleAsync(AppointmentCardQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(AppointmentCardQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];

        var appointment = await db.Appointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, cancellationToken)
            ?? throw new AppointmentNotFoundException(request.AppointmentId);

        var clientName = await db.Clients.AsNoTracking()
            .Where(c => c.Id == appointment.ClientId)
            .Select(c => c.FirstName + " " + c.LastName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var serviceName = await db.Services.AsNoTracking()
            .Where(s => s.Id == appointment.ServiceId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => (s.IsActive && s.IsBookable) || s.Id == appointment.StaffMemberId)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName)
            .Select(s => new CalendarStaff(s.Id, s.DisplayName, s.Colour))
            .ToListAsync(cancellationToken);
        var own = staff.FirstOrDefault(s => s.Id == appointment.StaffMemberId);

        var start = appointment.Start.InZone(zone);
        var isActive = AppointmentStateMachine.IsActive(appointment.Status);
        var moveStaffId = request.MoveStaffId is { } wanted && staff.Any(s => s.Id == wanted) ? wanted : appointment.StaffMemberId;
        var moveDate = request.MoveDate ?? start.Date;

        IReadOnlyList<MoveSlot> slots = [];
        if (isActive)
        {
            try
            {
                slots = (await availability.GetSlotsAsync(appointment.ServiceId, moveStaffId, moveDate, moveDate, appointment.Id, cancellationToken))
                    .Where(s => !(s.StaffMemberId == appointment.StaffMemberId && s.Start == appointment.Start))
                    .Select(s => new MoveSlot(s.Start, s.Start.InZone(zone).TimeOfDay))
                    .ToArray();
            }
            catch (InvalidOperationException)
            {
                // The service is no longer active: the visit stays as it is and offers no times.
            }
        }

        return new AppointmentCard(
            appointment.Id,
            start.Date,
            start.TimeOfDay,
            appointment.End.InZone(zone).TimeOfDay,
            clientName,
            serviceName,
            own?.DisplayName ?? string.Empty,
            appointment.StaffMemberId,
            own?.Colour ?? StaffMember.DefaultColour,
            appointment.Status,
            appointment.Price,
            appointment.Notes,
            CardActions.For(appointment.Status),
            isActive,
            staff,
            moveStaffId,
            moveDate,
            slots,
            appointment.ClientId,
            (await accounts.GetAccountAsync(appointment.ClientId, cancellationToken)).Owed);
    }
}