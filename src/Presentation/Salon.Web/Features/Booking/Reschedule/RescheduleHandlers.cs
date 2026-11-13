using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Booking;
using Salon.Services.Catalog;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Booking.Reschedule;

public sealed class RescheduleOptionsHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IPublicBookingService publicBooking,
    IAvailabilityService availability,
    IServiceOfferingResolver offerings,
    IClock clock) : IRequestHandler<RescheduleQuery, RescheduleOptions>
{
    public const int DaysShown = 14;

    public async Task<RescheduleOptions> HandleAsync(RescheduleQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(RescheduleQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;

        // Through the tenant filter: another salon's link is simply unknown here.
        var appointment = await publicBooking.FindByTokenAsync(request.Token, cancellationToken);
        if (appointment is null)
        {
            return RescheduleOptions.Without(null, RescheduleState.Invalid, tenant.Name, zone);
        }

        var now = clock.GetCurrentInstant();
        if (!appointment.IsActive)
        {
            return RescheduleOptions.Without(appointment, RescheduleState.NotActive, tenant.Name, zone);
        }

        if (appointment.Start <= now)
        {
            return RescheduleOptions.Without(appointment, RescheduleState.TooLate, tenant.Name, zone);
        }

        var bookable = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new RescheduleStaff(s.Id, s.DisplayName))
            .ToListAsync(cancellationToken);
        var offered = await offerings.ResolveAsync(appointment.ServiceId, bookable.Select(s => s.Id).ToArray(), cancellationToken);
        var staff = bookable.Where(s => offered[s.Id].Offers).ToList();

        // By default the person the visit is with now; "any" widens it; someone no longer bookable falls back to anyone.
        Guid? staffId = request.Staff switch
        {
            null or "" => appointment.StaffMemberId,
            RescheduleQuery.AnyStaff => null,
            _ => Guid.TryParse(request.Staff, out var parsed) ? parsed : appointment.StaffMemberId,
        };
        if (staffId is { } wanted && staff.All(s => s.Id != wanted))
        {
            staffId = null;
        }

        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken);
        var today = now.InZone(zone).Date;
        var dayCount = Math.Clamp(rules.MaxAdvanceDays + 1, 1, DaysShown);
        var last = today.PlusDays(dayCount - 1);
        // The time this visit holds now is free for it (the engine ignores it), but offering it back would be no change.
        var slots = (await availability.GetSlotsAsync(appointment.ServiceId, staffId, today, last, appointment.Id, cancellationToken))
            .Where(s => s.Start != appointment.Start)
            .ToList();
        var byDay = slots.ToLookup(s => s.Start.InZone(zone).Date);

        var days = Enumerable.Range(0, dayCount).Select(today.PlusDays).Select(d => new RescheduleDay(d, byDay[d].Any())).ToList();
        var current = appointment.Start.InZone(zone).Date;
        var selected = request.Date is { } requested && requested >= today && requested <= last
            ? requested
            : current >= today && current <= last && byDay[current].Any()
                ? current
                : days.FirstOrDefault(d => d.HasSlots)?.Date ?? today;

        return new RescheduleOptions(
            appointment,
            RescheduleState.Choose,
            tenant.Name,
            zone,
            days,
            selected,
            staff,
            staffId,
            byDay[selected].Select(s => new RescheduleSlot(s.Start, s.Start.InZone(zone).TimeOfDay)).DistinctBy(s => s.Start).ToList());
    }
}

public sealed class RescheduleHandler(IPublicBookingService publicBooking) : IRequestHandler<RescheduleCommand, PublicReschedule>
{
    public Task<PublicReschedule> HandleAsync(RescheduleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.RescheduleByTokenAsync(request.Token, request.StaffMemberId, request.NewStart, cancellationToken);
    }
}