using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Catalog;
using Salon.Services.Localization;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Web.Framework.Booking.Details;

public sealed class BookingSummaryHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IServiceOfferingResolver offerings,
    ILocalizationService localizer,
    IOnlineDepositCheckout onlineDeposits) : IRequestHandler<BookingSummaryQuery, BookingSummary?>
{
    public async Task<BookingSummary?> HandleAsync(BookingSummaryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await db.Services.AsNoTracking()
            .Where(s => s.Id == request.ServiceId && s.IsActive)
            .Select(s => new { s.Id, s.Name, s.Duration, s.Price, s.Currency, s.DepositPercent })
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

        // What the service and the salon ask for; a client's own rule (ADR 0023) is applied when the booking is made.
        var deposit = DepositRules.Amount(price.Amount, DepositRules.Percent(rules.DefaultDepositPercent, service.DepositPercent, null));

        // The further services of the visit (ADR 0095), each by the same rules, added to the first.
        var names = new List<string> { service.Name };
        // Who does each of them (ADR 0096): the person chosen for it, or nobody for "anyone".
        var people = new List<string?> { staffName };
        var extraIndex = 0;
        foreach (var extraId in request.ExtraServiceIds ?? [])
        {
            var extra = await db.Services.AsNoTracking()
                .Where(s => s.Id == extraId && s.IsActive)
                .Select(s => new { s.Id, s.Name, s.Duration, s.Price, s.Currency, s.DepositPercent })
                .FirstOrDefaultAsync(cancellationToken);
            if (extra is null)
            {
                return null;
            }

            var extraPrice = extra.Price;
            var extraDuration = extra.Duration;
            var extraStaff = request.ExtraStaffIds is { } chosenStaff && extraIndex < chosenStaff.Count ? chosenStaff[extraIndex] : null;
            extraIndex++;
            if (extraStaff is { } person)
            {
                var offering = await offerings.ResolveAsync(extra.Id, person, cancellationToken);
                if (!offering.Offers)
                {
                    return null;
                }

                extraDuration = offering.Duration;
                extraPrice = offering.Price;
            }
            else
            {
                var span = (await offerings.PriceSpansAsync([extra.Id], cancellationToken))[extra.Id];
                extraPrice = span.Min;
                priceIsFrom |= span.IsRange;
            }

            names.Add(extra.Name);
            people.Add(extraStaff is { } named ? await db.StaffMembers.AsNoTracking().Where(s => s.Id == named && s.IsActive && s.IsBookable).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken) : null);
            if (extraStaff is not null && people[^1] is null)
            {
                return null;
            }
            duration += extraDuration;
            price = price with { Amount = price.Amount + extraPrice };
            deposit += DepositRules.Amount(extraPrice, DepositRules.Percent(rules.DefaultDepositPercent, extra.DepositPercent, null));
        }

        return new BookingSummary(
            VisitRules.JoinNames(names),
            duration,
            price,
            priceIsFrom,
            // One name when one person does it all; otherwise each in the order of the services, "anyone" where none is named.
            people.Distinct().Count() == 1 ? staffName : string.Join(", ", people.Select(p => p ?? localizer["Booking.Public.Anyone"].ToString())),
            request.Start,
            DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc,
            tenant.Name,
            StartBookingHandler.ConsentWording(localizer, tenant.Name),
            rules.RequireSmsVerification,
            await settings.LoadAsync<CancellationPolicySettings>(cancellationToken),
            deposit > 0m ? price with { Amount = deposit } : null)
        {
            DepositPaidOnline = onlineDeposits.IsAvailable,
        };
    }
}