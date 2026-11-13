using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using NodaTime.Text;

using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.EditClient;

/// <summary>
/// Admin form at <c>/admin/clients/{id}/edit</c>. Tenant only, for signed-in users with <see cref="Permissions.ClientsView"/>
/// (ADR 0009, ADR 0011). GET shows the card's current values, health notes decrypted; a valid post lands back on the card
/// with a confirmation; an invalid one or a duplicate phone/email re-renders the form with the message next to its field.
/// An id the salon does not own is a 404 on both verbs.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/clients")]
public sealed class EditClientController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>ViewData key of the consent wording shown next to the checkbox (and recorded when newly ticked).</summary>
    public const string ConsentWordingKey = "ConsentWording";

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var card = await dispatcher.SendAsync(new EditClientQuery(id), cancellationToken);
        if (card is null)
        {
            return NotFound();
        }

        await PrepareConsentWordingAsync(cancellationToken);
        return View(FromCard(card));
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { Id = id };
        await PrepareConsentWordingAsync(cancellationToken);

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(command);
        }

        try
        {
            if (!await dispatcher.SendAsync(command, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(command);
        }
        catch (DuplicateClientException ex)
        {
            var key = ex.Field == nameof(Client.Email) ? "Clients.Edit.Validation.EmailTaken" : "Clients.Edit.Validation.PhoneTaken";
            ModelState.AddModelError(ex.Field ?? string.Empty, localizer[key]);
            return View(command);
        }

        SetFlash(localizer.Format("Clients.Edit.Saved", $"{command.FirstName.Trim()} {command.LastName.Trim()}"));
        return Redirect($"/admin/clients/{id}");
    }

    /// <summary>The form's initial values: what the card holds, the patch-test date as the ISO text a date input expects.</summary>
    private static EditClientCommand FromCard(Card card)
        => new(
            card.Id,
            card.FirstName,
            card.LastName,
            card.Phone,
            card.Email,
            card.Notes,
            card.HealthNotes,
            card.LastPatchTestOn is { } patchTest ? LocalDatePattern.Iso.Format(patchTest) : null,
            card.MarketingConsent,
            card.DepositPercent is { } percent ? (int)decimal.Round(percent) : null);

    private async Task PrepareConsentWordingAsync(CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        ViewData[ConsentWordingKey] = EditClientHandler.ConsentWording(localizer, tenant?.Name ?? string.Empty);
    }
}