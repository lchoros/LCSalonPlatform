using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// One availability call covers the whole strip (today plus up to <see cref="DaysShown"/> days, bounded by
/// <see cref="BookingSettings.MaxAdvanceDays"/>), so picking the default day and marking days with free times costs no
/// extra queries. Everything reads through the tenant filter; an unknown or inactive service is null (a 404 upstream).
/// </summary>
public sealed class ChooseSlotHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IAvailabilityService availability,
    IClock clock) : IRequestHandler<ChooseSlotQuery, ChooseSlotResult?>
{
    /// <summary>The date strip never shows more than two weeks; a salon open further ahead still books the rest later.</summary>
    public const int DaysShown = 14;

    public async Task<ChooseSlotResult?> HandleAsync(ChooseSlotQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await db.Services.AsNoTracking()
            .Where(s => s.Id == request.ServiceId && s.IsActive)
            .Select(s => new { s.Id, s.Name, s.Duration, s.Price, s.Currency })
            .FirstOrDefaultAsync(cancellationToken);
        if (service is null)
        {
            return null;
        }

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(ChooseSlotQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken);

        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new StaffOption(s.Id, s.DisplayName))
            .ToListAsync(cancellationToken);
        // A person who is no longer bookable falls back to "anyone" instead of an empty page.
        var staffId = request.StaffMemberId is { } wanted && staff.Any(s => s.Id == wanted) ? request.StaffMemberId : null;

        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var dayCount = Math.Clamp(rules.MaxAdvanceDays + 1, 1, DaysShown);
        var last = today.PlusDays(dayCount - 1);
        var slots = await availability.GetSlotsAsync(service.Id, staffId, today, last, cancellationToken);
        var byDay = slots.ToLookup(s => s.Start.InZone(zone).Date);

        var days = Enumerable.Range(0, dayCount).Select(today.PlusDays).Select(d => new DayOption(d, byDay[d].Any())).ToList();
        var selected = request.Date is { } requested && requested >= today && requested <= last
            ? requested
            : days.FirstOrDefault(d => d.HasSlots)?.Date ?? today;

        return new ChooseSlotResult(
            service.Id,
            service.Name,
            service.Duration,
            new Money(service.Price, service.Currency),
            days,
            selected,
            staff,
            staffId,
            byDay[selected].Select(s => new SlotOption(s.Start, s.Start.InZone(zone).TimeOfDay, s.StaffMemberId)).ToList(),
            zone);
    }
}