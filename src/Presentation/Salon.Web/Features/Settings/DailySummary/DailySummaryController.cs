using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Services.Notifications;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.DailySummary;

/// <summary>
/// <c>/admin/settings/daily-summary</c> (ADR 0050): whether the salon's owners get the day's numbers by email, and at
/// what time on the salon's clock. Behind <c>Settings.Manage</c>. The page shows what today's email would say now, so
/// the owner sees it before switching it on.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/daily-summary")]
public sealed class DailySummaryController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var page = await dispatcher.SendAsync(new GetDailySummaryQuery(), cancellationToken);
        return View(new DailySummaryViewModel(page.Form, page.Preview, TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SaveDailySummaryCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        try
        {
            await dispatcher.SendAsync(form, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            var page = await dispatcher.SendAsync(new GetDailySummaryQuery(), cancellationToken);
            return View(new DailySummaryViewModel(form, page.Preview, null));
        }

        SetFlash(localizer["Settings.DailySummary.Saved"]);
        return RedirectToAction(nameof(Index));
    }
}

/// <param name="Form">The switch and the time, as saved or as just typed.</param>
/// <param name="Preview">Today's summary as it would go out now.</param>
public sealed record DailySummaryViewModel(SaveDailySummaryCommand Form, DailySummaryPreview Preview, string? Flash)
{
    public bool HasRecipients => Preview.Recipients > 0;
}