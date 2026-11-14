using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.Notifications;

/// <summary>Reminder settings of the salon (ADR 0020): lead times and quiet hours, behind <c>Settings.Manage</c>.</summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/notifications")]
public sealed class NotificationSettingsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/settings/notifications";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var form = await dispatcher.SendAsync(new GetNotificationSettingsQuery(), cancellationToken);
        return View(new NotificationSettingsViewModel(form, TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SaveNotificationSettingsCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        // A value that did not even bind (letters in the hours) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(new NotificationSettingsViewModel(form, null));
        }

        try
        {
            await dispatcher.SendAsync(form, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(new NotificationSettingsViewModel(form, null));
        }

        SetFlash(localizer["Settings.Notifications.Saved"]);
        return Redirect(Path);
    }
}

public sealed record NotificationSettingsViewModel(SaveNotificationSettingsCommand Form, string? Flash);