using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.DayList;

/// <summary>
/// The booking service picks the day's active appointments; staff, client and service names come through the tenant
/// filter in three small lookups (a salon has tens of people and a day has tens of visits, not thousands). Health notes
/// are never read here (ADR 0011).
/// </summary>
public sealed class DayListHandler(
    SalonDbContext db,
    IBookingService booking,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<DayListQuery, DayBoard>
{
    public async Task<DayBoard> HandleAsync(DayListQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(DayListQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
        var day = request.Date ?? clock.GetCurrentInstant().InZone(zone).Date;

        var appointments = await booking.GetDayAsync(day, cancellationToken);
        var staffWithVisits = appointments.Select(a => a.StaffMemberId).Distinct().ToArray();
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => (s.IsActive && s.IsBookable) || staffWithVisits.Contains(s.Id))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName)
            .Select(s => new { s.Id, s.DisplayName, s.Colour })
            .ToListAsync(cancellationToken);

        var clientIds = appointments.Select(a => a.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .Select(c => new { c.Id, c.FirstName, c.LastName })
            .ToDictionaryAsync(c => c.Id, c => $"{c.FirstName} {c.LastName}", cancellationToken);
        var serviceIds = appointments.Select(a => a.ServiceId).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var columns = staff
            .Select(s => new StaffDay(
                s.Id,
                s.DisplayName,
                s.Colour,
                appointments.Where(a => a.StaffMemberId == s.Id)
                    .Select(a => new DayAppointment(
                        a.Id,
                        a.Start.InZone(zone).TimeOfDay,
                        a.End.InZone(zone).TimeOfDay,
                        clients.GetValueOrDefault(a.ClientId, string.Empty),
                        services.GetValueOrDefault(a.ServiceId, string.Empty),
                        a.Status))
                    .ToArray()))
            .ToArray();

        return new DayBoard(day, columns);
    }
}