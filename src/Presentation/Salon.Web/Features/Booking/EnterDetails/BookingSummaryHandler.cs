using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Catalog;
using Salon.Services.Localization;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Booking.EnterDetails;

public sealed class BookingSummaryHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IServiceOfferingResolver offerings,
    ILocalizationService localizer) : IRequestHandler<BookingSummaryQuery, BookingSummary?>
{
    public async Task<BookingSummary?> HandleAsync(BookingSummaryQuery request, CancellationToken cancellationToken)
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

        string? staffName = null;
        if (request.StaffMemberId is { } staffId)
        {
            staffName = await db.StaffMembers.AsNoTracking()
                .Where(s => s.Id == staffId && s.IsActive && s.IsBookable)
                .Select(s => s.DisplayName)
                .FirstOrDefaultAsync(cancellationToken);
            if (staffName is null)
            {
                return null;
            }
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(BookingSummaryQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken);

        // The chosen person's own price and duration, or the default with "from" under "anyone" (ADR 0018).
        var duration = service.Duration;
        var price = new Money(service.Price, service.Currency);
        var priceIsFrom = false;
        if (request.StaffMemberId is { } chosen)
        {
            var offering = await offerings.ResolveAsync(service.Id, chosen, cancellationToken);
            if (!offering.Offers)
            {
                return null;
            }

            duration = offering.Duration;
            price = new Money(offering.Price, offering.Currency);
        }
        else
        {
            var span = (await offerings.PriceSpansAsync([service.Id], cancellationToken))[service.Id];
            price = new Money(span.Min, service.Currency);
            priceIsFrom = span.IsRange;
        }

        return new BookingSummary(
            service.Name,
            duration,
            price,
            priceIsFrom,
            staffName,
            request.Start,
            DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc,
            tenant.Name,
            StartBookingHandler.ConsentWording(localizer, tenant.Name),
            rules.RequireSmsVerification,
            await settings.LoadAsync<CancellationPolicySettings>(cancellationToken));
    }
}