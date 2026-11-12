using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.CreateClient;

/// <summary>
/// Admin form at <c>/admin/clients/new</c>. Tenant only, for signed-in users with <see cref="Permissions.ClientsView"/>
/// (ADR 0009, ADR 0011). Post-redirect-get: a valid post lands on the new card with a confirmation; an invalid one or a
/// duplicate phone/email re-renders the form with the message next to its field.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/clients")]
public sealed class CreateClientController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>ViewData key of the consent wording shown next to the checkbox (and recorded when ticked).</summary>
    public const string ConsentWordingKey = "ConsentWording";

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        await PrepareConsentWordingAsync(cancellationToken);
        return View();
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CreateClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        await PrepareConsentWordingAsync(cancellationToken);

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(command);
        }

        Guid id;
        try
        {
            id = await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(command);
        }
        catch (DuplicateClientException ex)
        {
            var key = ex.Field == nameof(Client.Email) ? "Clients.Create.Validation.EmailTaken" : "Clients.Create.Validation.PhoneTaken";
            ModelState.AddModelError(ex.Field ?? string.Empty, localizer[key]);
            return View(command);
        }

        SetFlash(localizer.Format("Clients.Create.Created", $"{command.FirstName.Trim()} {command.LastName.Trim()}"));
        return Redirect($"/admin/clients/{id}");
    }

    private async Task PrepareConsentWordingAsync(CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        ViewData[ConsentWordingKey] = CreateClientHandler.ConsentWording(localizer, tenant?.Name ?? string.Empty);
    }
}