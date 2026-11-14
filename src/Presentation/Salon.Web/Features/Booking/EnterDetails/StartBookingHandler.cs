using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the verification row commits only if the SMS was handed to the
/// provider; on the no-verification path the client, appointment and outbox rows commit together. The consent wording
/// recorded is the label the form showed, in the request culture with the salon's name.
/// </summary>
public sealed class StartBookingHandler(
    IPublicBookingService publicBooking,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ILocalizationService localizer) : IRequestHandler<StartBookingCommand, PublicBookingStart>
{
    public async Task<PublicBookingStart> HandleAsync(StartBookingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has already refused anything that is not an ISO instant.
        var start = InstantPattern.ExtendedIso.Parse(request.Start?.Trim() ?? string.Empty);
        if (!start.Success)
        {
            throw new ArgumentException($"'{request.Start}' is not an ISO-8601 instant.", nameof(request));
        }

        string? wording = null;
        if (request.MarketingConsent)
        {
            var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(StartBookingCommand)} requires a tenant in scope.");
            var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
            wording = ConsentWording(localizer, tenant.Name);
        }

        return await publicBooking.StartAsync(
            new PublicBookingRequest(
                request.ServiceId,
                request.StaffMemberId,
                start.Value,
                request.FirstName ?? string.Empty,
                request.LastName ?? string.Empty,
                request.Phone ?? string.Empty,
                request.Email,
                request.MarketingConsent,
                wording),
            cancellationToken);
    }

    /// <summary>The checkbox label on the form; the same text lands on the client card as what was agreed to.</summary>
    public static string ConsentWording(ILocalizationService localizer, string salonName)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer.Format("Booking.Public.ConsentWording", salonName);
    }
}