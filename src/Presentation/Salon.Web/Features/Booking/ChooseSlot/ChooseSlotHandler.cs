using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Catalog;
using Salon.Services.Settings;
using Salon.Services.Staff;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Ui;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// Step 2 of the public flow (ADR 0014): the people who offer the service, two weeks of days, the slots of one day. Price
/// and duration are the chosen person's own (ADR 0018); under "anyone" the service default with "from" when people differ.
/// </summary>
public sealed class ChooseSlotHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IAvailabilityService availability,
    IServiceOfferingResolver offerings,
    IStaffPhotoService photos,
    IClock clock) : IRequestHandler<ChooseSlotQuery, ChooseSlotResult?>
{
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

        var bookable = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new StaffOption(s.Id, s.DisplayName))
            .ToListAsync(cancellationToken);
        // The further services of a visit booked as several (ADR 0095), in order; one that is unknown or no longer
        // active quietly drops out, like a person who is no longer bookable.
        var wantedExtras = (request.ExtraServiceIds ?? []).Where(id => id != service.Id).Distinct().Take(VisitRules.MaxServices - 1).ToList();
        var foundExtras = await db.Services.AsNoTracking()
            .Where(s => wantedExtras.Contains(s.Id) && s.IsActive)
            .Select(s => new { s.Id, s.Name, s.Duration, s.Price, s.Currency })
            .ToListAsync(cancellationToken);
        var parts = new[] { service }.Concat(wantedExtras.Select(id => foundExtras.FirstOrDefault(s => s.Id == id)).Where(s => s is not null).Select(s => s!)).ToList();
        var serviceIds = parts.Select(p => p.Id).ToList();

        // Someone who does not offer a service is not on the picker at all (ADR 0018); the engine skips them too. For
        // several services one person does the whole visit, so only people who offer every one of them are shown.
        var bookableIds = bookable.Select(s => s.Id).ToArray();
        var offeredBy = new Dictionary<Guid, IReadOnlyDictionary<Guid, ServiceOffering>>();
        foreach (var part in parts)
        {
            offeredBy[part.Id] = await offerings.ResolveAsync(part.Id, bookableIds, cancellationToken);
        }

        // Faces on the picker (ADR 0088): who has a picture and in which version; the pictures themselves are fetched by the browser.
        var photoVersions = await photos.VersionsAsync(cancellationToken);
        var staff = bookable.Where(s => parts.All(p => offeredBy[p.Id][s.Id].Offers)).Select(s => s with { PhotoPath = StaffPhotoAddress.For(s.Id, photoVersions) }).ToList();
        // A person who is no longer bookable (or no longer offers it) falls back to the question instead of an empty page.
        var staffId = request.StaffMemberId is { } wanted && staff.Any(s => s.Id == wanted) ? request.StaffMemberId : null;
        // Who with is asked, not assumed (the owner, 2026-11-22): with a choice of people, the client has to say before going on.
        var needsChoice = staff.Count > 1 && staffId is null && !request.Anyone;

        // Each service at the chosen person's own price and duration, or the cheapest with "from" until a person is
        // chosen (ADR 0018); the visit is their sum.
        var spans = staffId is null ? await offerings.PriceSpansAsync(serviceIds, cancellationToken) : null;
        var lines = parts
            .Select(p =>
            {
                if (staffId is { } chosen)
                {
                    var offering = offeredBy[p.Id][chosen];
                    return new VisitLine(p.Id, p.Name, offering.Duration, new Money(offering.Price, offering.Currency), false);
                }

                var span = spans![p.Id];
                return new VisitLine(p.Id, p.Name, p.Duration, new Money(span.Min, p.Currency), span.IsRange);
            })
            .ToList();
        var duration = lines.Aggregate(Duration.Zero, (sum, l) => sum + l.Duration);
        var price = new Money(lines.Sum(l => l.Price.Amount), service.Currency);
        var priceIsFrom = lines.Any(l => l.PriceIsFrom);

        // The strip is a window of up to two weeks that slides between today and the salon's booking horizon.
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var horizon = today.PlusDays(Math.Max(rules.MaxAdvanceDays, 0));
        var windowStart = request.From is { } from && from > today ? LocalDate.Min(from, horizon) : today;
        var last = LocalDate.Min(windowStart.PlusDays(DaysShown - 1), horizon);
        var dayCount = Period.DaysBetween(windowStart, last) + 1;
        // Until "who with" is answered the times are those of anyone who could do it (the owner, 2026-11-25): the
        // page shows them from the start, and the answer is asked for when the client goes on.
        var slots = staff.Count == 0 ? [] : await availability.GetVisitSlotsAsync(serviceIds, staffId, windowStart, last, cancellationToken);
        var byDay = slots.ToLookup(s => s.Start.InZone(zone).Date);

        var days = Enumerable.Range(0, dayCount).Select(windowStart.PlusDays).Select(d => new DayOption(d, byDay[d].Any())).ToList();
        var selected = request.Date is { } requested && requested >= windowStart && requested <= last
            ? requested
            : days.FirstOrDefault(d => d.HasSlots)?.Date ?? windowStart;

        var daySlots = byDay[selected].Select(s => new SlotOption(s.Start, s.Start.InZone(zone).TimeOfDay, s.StaffMemberId)).ToList();
        var groups = Grouped(daySlots);
        // Only a start that is genuinely free right now counts; a stale or foreign one quietly deselects.
        var chosenSlot = request.Start is { } wantedStart ? daySlots.FirstOrDefault(s => s.Start == wantedStart) : null;
        var chosenPart = chosenSlot is null ? 0 : Math.Max(0, groups.ToList().FindIndex(g => g.Slots.Contains(chosenSlot)));
        return new ChooseSlotResult(
            service.Id,
            service.Name,
            duration,
            price,
            priceIsFrom,
            days,
            selected,
            staff,
            staffId,
            daySlots,
            zone)
        {
            NeedsStaffChoice = needsChoice,
            DayAsked = request.Date == selected,
            AnyoneChosen = staffId is null && !needsChoice,
            // The waiting list is for one service (ADR 0055); it is not offered for a visit of several.
            WaitlistOffered = rules.PublicWaitlist && lines.Count == 1,
            Lines = lines,
            NobodyOffersAll = lines.Count > 1 && staff.Count == 0,
            EarlierFrom = windowStart > today ? LocalDate.Max(today, windowStart.PlusDays(-DaysShown)) : null,
            LaterFrom = last < horizon ? last.PlusDays(1) : null,
            Groups = groups,
            Chosen = chosenSlot,
            ChosenGroup = chosenPart,
        };
    }

    /// <summary>Splits one day's free times into parts of the day, so a long list scans by lunch and after work.</summary>
    internal static IReadOnlyList<SlotGroup> Grouped(IReadOnlyList<SlotOption> slots)
    {
        var parts = new (string LabelKey, Func<LocalTime, bool> Holds)[]
        {
            ("Booking.Public.Slots.Morning", t => t.Hour < 12),
            ("Booking.Public.Slots.Midday", t => t.Hour is >= 12 and < 16),
            ("Booking.Public.Slots.Evening", t => t.Hour >= 16),
        };
        return parts
            .Select(p => new SlotGroup(p.LabelKey, slots.Where(s => p.Holds(s.LocalStart)).ToList()))
            .Where(g => g.Slots.Count > 0)
            .ToList();
    }
}