using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.CancellationPolicy;

/// <summary>
/// The salon's cancellation policy (ADR 0022): the notice period for free changes and the fees a late cancellation or a
/// missed visit may carry, behind <c>Settings.Manage</c>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/cancellation-policy")]
public sealed class CancellationPolicyController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var form = await dispatcher.SendAsync(new GetCancellationPolicyQuery(), cancellationToken);
        return View(new CancellationPolicyViewModel(form, TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SaveCancellationPolicyCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        // A value that did not even bind (letters in a number) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(new CancellationPolicyViewModel(form, null));
        }

        try
        {
            await dispatcher.SendAsync(form, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(new CancellationPolicyViewModel(form, null));
        }

        SetFlash(localizer["Settings.CancellationPolicy.Saved"]);
        return RedirectToAction(nameof(Index));
    }
}

public sealed record CancellationPolicyViewModel(SaveCancellationPolicyCommand Form, string? Flash);