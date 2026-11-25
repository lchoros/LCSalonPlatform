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
    public Task<IReadOnlyList<Slot>> GetSlotsAsync(Guid serviceId, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default)
        => GetSlotsAsync(serviceId, staffMemberId, fromDate, toDate, null, cancellationToken);

    public Task<IReadOnlyList<Slot>> GetSlotsAsync(Guid serviceId, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, Guid? excludeAppointmentId, CancellationToken cancellationToken = default)
        => SlotsAsync([serviceId], staffMemberId, fromDate, toDate, excludeAppointmentId, cancellationToken);

    public Task<IReadOnlyList<Slot>> GetVisitSlotsAsync(IReadOnlyList<Guid> serviceIds, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(serviceIds);
        if (serviceIds.Count == 0 || serviceIds.Distinct().Count() != serviceIds.Count)
        {
            throw new ArgumentException("A visit needs at least one service and each service once.", nameof(serviceIds));
        }

        return SlotsAsync(serviceIds, staffMemberId, fromDate, toDate, null, cancellationToken);
    }

    private async Task<IReadOnlyList<Slot>> SlotsAsync(IReadOnlyList<Guid> serviceIds, Guid? staffMemberId, LocalDate fromDate, LocalDate toDate, Guid? excludeAppointmentId, CancellationToken cancellationToken)
    {
        if (toDate < fromDate)
        {
            throw new ArgumentException("The date range ends before it starts.", nameof(toDate));
        }

        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var durations = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id) && s.IsActive)
            .Select(s => new { s.Id, s.Duration })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (serviceIds.FirstOrDefault(id => durations.All(d => d.Id != id)) is { } missing && missing != Guid.Empty)
        {
            throw new InvalidOperationException($"Service {missing} does not exist or is not active in this salon.");
        }

        // One service after another without a gap: the visit is as long as its services together.
        var listDuration = durations.Aggregate(Duration.Zero, (sum, d) => sum + d.Duration);
        var settings = await settingService.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);

        var staffIds = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable && (staffMemberId == null || s.Id == staffMemberId))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        // Staff-specific durations; people who do not offer a service drop out here, also under "any staff" (ADR 0018).
        // For a visit of several services a person has to offer every one of them (ADR 0095).
        var ownDuration = staffIds.ToDictionary(id => id, _ => Duration.Zero);
        foreach (var serviceId in serviceIds)
        {
            var offered = await offerings.ResolveAsync(serviceId, staffIds, cancellationToken).ConfigureAwait(false);
            staffIds = staffIds.Where(id => offered[id].Offers).ToList();
            foreach (var id in staffIds)
            {
                ownDuration[id] += offered[id].Duration;
            }
        }

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
            .Where(a => staffIds.Contains(a.StaffMemberId) && active.Contains(a.Status) && a.Start < windowEnd && a.End > busyFrom && (excludeAppointmentId == null || a.Id != excludeAppointmentId))
            .Select(a => new { a.StaffMemberId, a.Start, a.End })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Time off (ADR 0015) is busy time like an appointment: every entry whose local date span touches the window, one
        // interval per day in the salon's zone. The pure calculator excludes busy intervals, so nothing else changes.
        var timeOff = await db.StaffTimeOff.AsNoTracking()
            .Where(t => staffIds.Contains(t.StaffMemberId) && t.FromDate <= toDate && t.ToDate >= fromDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A freed time kept for a waiting client (ADR 0065) is busy for everybody else until the hold runs out or ends.
        var now = clock.GetCurrentInstant();
        var held = await db.WaitlistOffers.AsNoTracking()
            .Where(o => staffIds.Contains(o.StaffMemberId) && o.EndedAt == null && o.HeldUntil > now && o.Start < windowEnd && o.End > busyFrom)
            .Select(o => new { o.StaffMemberId, o.Start, o.End })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var staff = staffIds
            .Select(id => new StaffAvailabilityInput(
                id,
                hours.Where(h => h.StaffMemberId == id)
                    .GroupBy(h => h.Day)
                    .ToDictionary(g => g.Key, g => (IReadOnlyList<TimeRange>)g.Select(h => h.Range).OrderBy(r => r.Start).ToArray()),
                appointments.Where(a => a.StaffMemberId == id).Select(a => new Interval(a.Start, a.End.Plus(buffer)))
                    .Concat(timeOff.Where(t => t.StaffMemberId == id).SelectMany(t => StaffTimeOffRules.ToIntervals(t, zone)))
                    .Concat(held.Where(h => h.StaffMemberId == id).Select(h => new Interval(h.Start, h.End.Plus(buffer))))
                    .ToArray(),
                ownDuration[id]))
            .ToArray();

        var request = new AvailabilityRequest(
            zone,
            fromDate,
            toDate,
            listDuration,
            buffer,
            settings.SlotStepMinutes,
            now,
            Duration.FromMinutes(settings.MinNoticeMinutes),
            settings.MaxAdvanceDays,
            staff);

        return staffMemberId is null ? AvailabilityCalculator.ForAnyStaff(request) : AvailabilityCalculator.Slots(request);
    }
}