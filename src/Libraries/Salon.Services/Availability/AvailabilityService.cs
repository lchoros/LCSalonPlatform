using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Settings;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Catalog;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Availability;

internal sealed class AvailabilityService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settingService,
    IServiceOfferingResolver offerings,
    IClock clock) : IAvailabilityService
{
    public async Task<IReadOnlyList<Slot>> GetSlotsAsync(Guid serviceId, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("The date range ends before it starts.", nameof(toDate));
        }

        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var service = await db.Services.AsNoTracking()
            .Where(s => s.Id == serviceId && s.IsActive)
            .Select(s => new { s.Duration })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Service {serviceId} does not exist or is not active in this salon.");
        var settings = await settingService.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);

        var staffIds = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable && (staffMemberId == null || s.Id == staffMemberId))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // Staff-specific durations; people who do not offer this service drop out here, also under "any staff" (ADR 0018).
        var offered = await offerings.ResolveAsync(serviceId, staffIds, cancellationToken).ConfigureAwait(false);
        staffIds = staffIds.Where(id => offered[id].Offers).ToList();
        if (staffIds.Count == 0)
        {
            return [];
        }

        var hours = await db.WorkingHours.AsNoTracking()
            .Where(w => staffIds.Contains(w.StaffMemberId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = Duration.FromMinutes(settings.BufferAfterMinutes);
        var windowStart = zone.AtStartOfDay(fromDate).ToInstant();
        var windowEnd = zone.AtStartOfDay(toDate.PlusDays(1)).ToInstant();
        // An appointment that ends just before the window still blocks its buffer inside it.
        var busyFrom = windowStart.Minus(buffer);
        var active = AppointmentStateMachine.Active.ToArray();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => staffIds.Contains(a.StaffMemberId) && active.Contains(a.Status) && a.Start < windowEnd && a.End > busyFrom)
            .Select(a => new { a.StaffMemberId, a.Start, a.End })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var staff = staffIds
            .Select(id => new StaffAvailabilityInput(
                id,
                hours.Where(h => h.StaffMemberId == id)
                    .GroupBy(h => h.Day)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<TimeRange>)g.Select(h => h.Range).OrderBy(r => r.Start).ToArray()),
                appointments.Where(a => a.StaffMemberId == id).Select(a => new Interval(a.Start, a.End.Plus(buffer))).ToArray(),
                offered[id].Duration))
            .ToArray();

        var request = new AvailabilityRequest(
            zone,
            fromDate,
            toDate,
            service.Duration,
            buffer,
            settings.SlotStepMinutes,
            clock.GetCurrentInstant(),
            Duration.FromMinutes(settings.MinNoticeMinutes),
            settings.MaxAdvanceDays,
            staff);

        return staffMemberId is null ? AvailabilityCalculator.ForAnyStaff(request) : AvailabilityCalculator.Slots(request);
    }
}