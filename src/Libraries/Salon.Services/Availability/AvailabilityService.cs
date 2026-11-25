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

    public async Task<IReadOnlyList<VisitSlot>> GetVisitPlanAsync(IReadOnlyList<VisitPart> parts, LocalDate fromDate, LocalDate toDate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var serviceIds = parts.Select(p => p.ServiceId).ToList();
        if (serviceIds.Count == 0 || serviceIds.Distinct().Count() != serviceIds.Count)
        {
            throw new ArgumentException("A visit needs at least one service and each service once.", nameof(parts));
        }

        if (toDate < fromDate)
        {
            throw new ArgumentException("The date range ends before it starts.", nameof(toDate));
        }

        // One person for every part, or nobody named at all for a single service: the plain engine answers.
        if (parts.Count == 1 || (parts[0].StaffMemberId is { } one && parts.All(p => p.StaffMemberId == one)))
        {
            var plain = await SlotsAsync(serviceIds, parts[0].StaffMemberId, fromDate, toDate, null, cancellationToken).ConfigureAwait(false);
            return [.. plain.Select(s => new VisitSlot(s.Start, s.End, [.. parts.Select(_ => s.StaffMemberId)]))];
        }

        var zone = await TenantZone.ResolveAsync(tenantContext, tenantLookup, cancellationToken).ConfigureAwait(false);
        var active = await db.Services.AsNoTracking().Where(s => serviceIds.Contains(s.Id) && s.IsActive).Select(s => s.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (serviceIds.FirstOrDefault(id => !active.Contains(id)) is { } missing && missing != Guid.Empty)
        {
            throw new InvalidOperationException($"Service {missing} does not exist or is not active in this salon.");
        }

        var settings = await settingService.LoadAsync<BookingSettings>(cancellationToken).ConfigureAwait(false);
        var staffIds = await BookableStaffAsync(null, cancellationToken).ConfigureAwait(false);
        var offered = new Dictionary<Guid, IReadOnlyDictionary<Guid, ServiceOffering>>();
        foreach (var serviceId in serviceIds)
        {
            offered[serviceId] = await offerings.ResolveAsync(serviceId, staffIds, cancellationToken).ConfigureAwait(false);
        }

        // Who may do each part: the person named, if they are bookable and offer it, or everybody who offers it.
        var candidates = parts
            .Select(p => p.StaffMemberId is { } named
                ? staffIds.Where(id => id == named && offered[p.ServiceId][id].Offers).ToList()
                : staffIds.Where(id => offered[p.ServiceId][id].Offers).ToList())
            .ToList();
        if (candidates.Any(c => c.Count == 0))
        {
            return [];
        }

        var (inputs, now, buffer) = await InputsAsync(staffIds, zone, settings, fromDate, toDate, null, _ => null, cancellationToken).ConfigureAwait(false);
        var byId = inputs.ToDictionary(i => i.StaffMemberId);

        // Where the visit may begin: every start the engine offers for the first part with any of its people, so the
        // notice, the booking horizon and the step of the clock are the engine's own.
        var first = parts[0];
        var starts = AvailabilityCalculator.Slots(new AvailabilityRequest(
                zone,
                fromDate,
                toDate,
                offered[first.ServiceId][candidates[0][0]].Duration,
                buffer,
                settings.SlotStepMinutes,
                now,
                Duration.FromMinutes(settings.MinNoticeMinutes),
                settings.MaxAdvanceDays,
                [.. candidates[0].Select(id => byId[id] with { ServiceDuration = offered[first.ServiceId][id].Duration })]))
            .Select(s => s.Start)
            .Distinct()
            .OrderBy(s => s)
            .ToList();

        // People with a quieter day first, as the engine does for "anyone", then the salon's own order.
        var busyOn = new Dictionary<(Guid, LocalDate), int>();
        foreach (var input in inputs)
        {
            foreach (var busy in input.Busy)
            {
                var key = (input.StaffMemberId, busy.Start.InZone(zone).Date);
                busyOn[key] = busyOn.GetValueOrDefault(key) + 1;
            }
        }

        var plan = new List<VisitSlot>();
        foreach (var start in starts)
        {
            var day = start.InZone(zone).Date;
            IEnumerable<Guid> Quietest(IEnumerable<Guid> people) => people.OrderBy(id => busyOn.GetValueOrDefault((id, day))).ThenBy(staffIds.IndexOf);
            // One person for the whole visit first, where the parts left open allow it: no change of hands.
            var whole = Quietest(candidates.Skip(1).Aggregate(candidates[0].AsEnumerable(), (all, next) => all.Intersect(next))).Select(person =>
            {
                var cursor = start;
                foreach (var part in parts)
                {
                    var length = offered[part.ServiceId][person].Duration;
                    if (!AvailabilityCalculator.IsFree(zone, byId[person], cursor, length.Plus(buffer)))
                    {
                        return ((Guid Person, Instant End)?)null;
                    }

                    cursor = cursor.Plus(length);
                }

                return (person, cursor);
            }).FirstOrDefault(found => found is not null);
            if (whole is { } alone)
            {
                plan.Add(new VisitSlot(start, alone.End, [.. parts.Select(_ => alone.Person)]));
                continue;
            }

            var chosen = new Guid[parts.Count];
            if (Assign(0, start, null) is { } end)
            {
                plan.Add(new VisitSlot(start, end, [.. chosen]));
            }

            // Depth first over the parts: the person of the part before is tried first, so a visit changes hands only
            // where it has to. Returns the end of the visit, or null when this start cannot be filled.
            Instant? Assign(int index, Instant from, Guid? before)
            {
                if (index == parts.Count)
                {
                    return from;
                }

                var people = Quietest(candidates[index]);
                foreach (var person in before is { } same && candidates[index].Contains(same) ? people.OrderByDescending(id => id == same) : people)
                {
                    var length = offered[parts[index].ServiceId][person].Duration;
                    // The buffer after a part is the person's own time before their next client; the visit itself goes on.
                    if (!AvailabilityCalculator.IsFree(zone, byId[person], from, length.Plus(buffer)))
                    {
                        continue;
                    }

                    chosen[index] = person;
                    if (Assign(index + 1, from.Plus(length), person) is { } done)
                    {
                        return done;
                    }
                }

                return null;
            }
        }

        return plan;
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

        var staffIds = await BookableStaffAsync(staffMemberId, cancellationToken).ConfigureAwait(false);
        // Staff-specific durations; people who do not offer a service drop out here, also under "any staff" (ADR 0018).
        // Here one person does the whole visit, so they have to offer every one of its services (ADR 0095).
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

        var (staff, now, buffer) = await InputsAsync(staffIds, zone, settings, fromDate, toDate, excludeAppointmentId, id => ownDuration[id], cancellationToken).ConfigureAwait(false);
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

    /// <summary>The active, bookable people in the salon's order; one of them when a person is named.</summary>
    private async Task<List<Guid>> BookableStaffAsync(Guid? staffMemberId, CancellationToken cancellationToken)
        => await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable && (staffMemberId == null || s.Id == staffMemberId))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>What the engine needs to know about each person in the window: when they work and when they are busy.</summary>
    private async Task<(StaffAvailabilityInput[] Staff, Instant Now, Duration Buffer)> InputsAsync(
        List<Guid> staffIds,
        DateTimeZone zone,
        BookingSettings settings,
        LocalDate fromDate,
        LocalDate toDate,
        Guid? excludeAppointmentId,
        Func<Guid, Duration?> ownDuration,
        CancellationToken cancellationToken)
    {
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
                ownDuration(id)))
            .ToArray();
        return (staff, now, buffer);
    }
}