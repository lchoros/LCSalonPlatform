using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Clients.EditClient;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): details, health notes and consent are rewritten together or not at
/// all. Consent is only touched when the tick changed, so an existing grant keeps its original wording and instant as
/// proof; a new grant records the label the form showed, in the request culture with the salon's name.
/// </summary>
public sealed class EditClientHandler(
    IClientService clients,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ILocalizationService localizer) : IRequestHandler<EditClientCommand, bool>
{
    public async Task<bool> HandleAsync(EditClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Through the tenant filter: another salon's client id is "not found", never "updated".
        var card = await clients.GetCardAsync(request.Id, cancellationToken);
        if (card is null)
        {
            return false;
        }

        await clients.UpdateDetailsAsync(request.Id, request.FirstName, request.LastName, request.Phone, request.Email, request.Notes, cancellationToken);
        await clients.UpdateHealthNotesAsync(request.Id, request.HealthNotes, ParseDate(request.LastPatchTestOn), cancellationToken);
        await clients.SetDepositPercentAsync(request.Id, request.DepositPercent, cancellationToken);

        if (card.MarketingConsent != request.MarketingConsent)
        {
            string? wording = null;
            if (request.MarketingConsent)
            {
                // The tenant guard has already refused tenant-less requests; this only narrows the type.
                var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(EditClientCommand)} requires a tenant in scope.");
                var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
                    ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
                wording = ConsentWording(localizer, tenant.Name);
            }

            await clients.SetMarketingConsentAsync(request.Id, request.MarketingConsent, wording, cancellationToken);
        }

        return true;
    }

    /// <summary>The checkbox label on the form; the same text as on the create form, so the card shows one wording for both.</summary>
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