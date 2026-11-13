using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.SalonDetails;

/// <summary>
/// <c>/admin/settings/salon-details</c> (ADR 0027): the address and contact details of the salon's main location,
/// behind <c>Settings.Manage</c>. Clients see them when they book and at the end of their emails.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/salon-details")]
public sealed class SalonDetailsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var form = await dispatcher.SendAsync(new GetSalonDetailsQuery(), cancellationToken);
        return View(new SalonDetailsViewModel(form, TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SaveSalonDetailsCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        try
        {
            await dispatcher.SendAsync(form, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(new SalonDetailsViewModel(form, null));
        }

        SetFlash(localizer["Settings.SalonDetails.Saved"]);
        return RedirectToAction(nameof(Index));
    }
}

public sealed record SalonDetailsViewModel(SaveSalonDetailsCommand Form, string? Flash);