using NodaTime;
using NodaTime.Text;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Clients.CreateClient;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the client row and the ClientCreated outbox row commit together or not
/// at all. The consent wording recorded is the label the form showed, in the request culture with the salon's name.
/// </summary>
public sealed class CreateClientHandler(
    IClientService clients,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ILocalizationService localizer) : IRequestHandler<CreateClientCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? wording = null;
        if (request.MarketingConsent)
        {
            // The tenant guard has already refused tenant-less requests; this only narrows the type.
            var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(CreateClientCommand)} requires a tenant in scope.");
            var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
                ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
            wording = ConsentWording(localizer, tenant.Name);
        }

        return await clients.CreateAsync(
            new CreateClientRequest(
                request.FirstName,
                request.LastName,
                request.Phone,
                request.Email,
                request.Notes,
                request.HealthNotes,
                ParseDate(request.LastPatchTestOn),
                request.MarketingConsent,
                wording),
            cancellationToken);
    }

    /// <summary>The checkbox label on the form; the same text is what the card shows as agreed to.</summary>
    public static string ConsentWording(ILocalizationService localizer, string salonName)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return localizer.Format("Clients.Create.MarketingConsentWording", salonName);
    }

    private static LocalDate? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        // The validator has already refused anything that is not an ISO date.
        var result = LocalDatePattern.Iso.Parse(iso.Trim());
        return result.Success ? result.Value : throw new ArgumentException($"'{iso}' is not an ISO date.", nameof(iso));
    }
}