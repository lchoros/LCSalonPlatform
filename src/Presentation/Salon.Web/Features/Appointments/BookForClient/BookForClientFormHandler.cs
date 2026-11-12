using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Availability;
using Salon.Services.Catalog;
using Salon.Services.Clients;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>
/// Reads through the tenant filter only: the client service for the picker (never health notes, ADR 0011), the catalogue
/// and staff tables for the selects, and the availability service for the slots (ADR 0012). A selection that no longer
/// exists (a service made inactive, a staff member deactivated) is simply dropped rather than failing the page.
/// </summary>
public sealed class BookForClientFormHandler(
    SalonDbContext db,
    IClientService clients,
    IAvailabilityService availability,
    IServiceOfferingResolver offerings,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<BookForClientFormQuery, BookForClientForm>
{
    /// <summary>Enough to recognise the right person on a phone screen; a longer query narrows it further.</summary>
    public const int MaxMatches = 10;

    public async Task<BookForClientForm> HandleAsync(BookForClientFormQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(BookForClientFormQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
        var day = request.Date ?? clock.GetCurrentInstant().InZone(zone).Date;

        var matches = request.Query is { Length: > 0 }
            ? await clients.SearchAsync(request.Query, MaxMatches, cancellationToken)
            : [];
        ClientSummary? client = null;
        if (request.ClientId is { } clientId)
        {
            client = await db.Clients.AsNoTracking()
                .Where(c => c.Id == clientId)
                .Select(c => new ClientSummary(c.Id, c.FirstName, c.LastName, c.Phone, c.Email, c.IsActive))
                .FirstOrDefaultAsync(cancellationToken);
        }

        var services = await db.Services.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new ServiceOption(s.Id, s.Name, (int)s.Duration.TotalMinutes, s.Price, s.Currency))
            .ToListAsync(cancellationToken);
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName)
            .Select(s => new StaffOption(s.Id, s.DisplayName))
            .ToListAsync(cancellationToken);

        var serviceId = services.Any(s => s.Id == request.ServiceId) ? request.ServiceId : null;
        var staffMemberId = staff.Any(s => s.Id == request.StaffMemberId) ? request.StaffMemberId : null;
        var slots = serviceId is { } chosenService
            ? (await availability.GetSlotsAsync(chosenService, staffMemberId, day, day, cancellationToken))
                .Select(s => new SlotOption(s.StaffMemberId, staff.First(p => p.Id == s.StaffMemberId).DisplayName, s.Start, s.Start.InZone(zone).TimeOfDay))
                .ToArray()
            : [];
        var offering = serviceId is { } chosen
            ? await SummarizeAsync(services.First(s => s.Id == chosen), staffMemberId is { } id ? staff.First(p => p.Id == id) : null, cancellationToken)
            : null;

        return new BookForClientForm(request.Query, matches, client, services, staff, serviceId, staffMemberId, day, slots, offering);
    }

    /// <summary>The chosen person's own duration and price (ADR 0018); under "any", the list duration and the cheapest price with "from" when staff differ.</summary>
    private async Task<OfferingSummary> SummarizeAsync(ServiceOption service, StaffOption? person, CancellationToken cancellationToken)
    {
        if (person is null)
        {
            var span = (await offerings.PriceSpansAsync([service.Id], cancellationToken)).GetValueOrDefault(service.Id, new PriceSpan(service.Price, service.Price));
            return new OfferingSummary(service.DurationMinutes, new Money(span.Min, service.Currency), span.IsRange, true, null);
        }

        var offering = await offerings.ResolveAsync(service.Id, person.Id, cancellationToken);
        return new OfferingSummary((int)offering.Duration.TotalMinutes, new Money(offering.Price, offering.Currency), false, offering.Offers, person.DisplayName);
    }
}