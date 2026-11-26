using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Localization;
using Salon.Services.Notifications;
using Salon.Services.Settings;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Booking.Details;

namespace Salon.Web.Features.Api.Booking.ShowTerms;

/// <summary>
/// GET terms of the booking API (ADR 0097): what the details page says before the client commits — whether a code is
/// texted, the words beside the marketing tick (recorded with a yes), the privacy notice and the cancellation policy. A query.
/// </summary>
public sealed record BookingTermsQuery : IQuery<BookingTerms>;

public sealed class BookingTermsHandler(
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    ILocalizationService localizer,
    IPublicUrlBuilder urls) : IRequestHandler<BookingTermsQuery, BookingTerms>
{
    public async Task<BookingTerms> HandleAsync(BookingTermsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(BookingTermsQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken);
        var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
        // The same words the details form shows, so the same words land on the client card (StartBookingHandler).
        var wording = StartBookingHandler.ConsentWording(localizer, tenant.Name);
        return new BookingTerms(
            rules.RequireSmsVerification,
            wording,
            urls.SalonUrl(tenant.Slug, PublicBookingDefaults.PrivacyPath),
            policy.FreeChangeHours,
            policy.LateCancellationFeePercent,
            policy.NoShowFeePercent);
    }
}