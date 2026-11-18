using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Services.Localization;
using Salon.Services.Privacy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.EditPrivacyNotice;

/// <summary>Which notice the salon shows its clients, and its own text if it has one (ADR 0063).</summary>
public sealed record PrivacyNoticeChoiceQuery : IQuery<PrivacyNoticeChoice>;

/// <summary>The choice as posted. An unticked box is not posted, which is "the standard notice".</summary>
/// <param name="UseOwnText">Show the salon's own text in place of the standard notice.</param>
/// <param name="OwnText">The salon's own notice; kept even while the standard one is shown.</param>
public sealed record SavePrivacyNoticeCommand(bool UseOwnText, string? OwnText) : ICommand<PrivacyNoticeOutcome>;

public sealed class EditPrivacyNoticeHandler(IPrivacyNoticeService notices) :
    IRequestHandler<PrivacyNoticeChoiceQuery, PrivacyNoticeChoice>,
    IRequestHandler<SavePrivacyNoticeCommand, PrivacyNoticeOutcome>
{
    public Task<PrivacyNoticeChoice> HandleAsync(PrivacyNoticeChoiceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return notices.GetChoiceAsync(cancellationToken);
    }

    public Task<PrivacyNoticeOutcome> HandleAsync(SavePrivacyNoticeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return notices.SaveAsync(new PrivacyNoticeChoice(request.UseOwnText, request.OwnText), cancellationToken);
    }
}

/// <summary>
/// <c>/admin/settings/privacy</c> (ADR 0063): the owner chooses between the standard privacy notice and their own
/// text. The standard notice says what this system does with client details; it is a starting text, not legal
/// advice, and the page says so. Behind <c>Settings.Manage</c>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/privacy")]
public sealed class EditPrivacyNoticeController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The notice as clients read it; the ShowPrivacyNotice slice owns the route.</summary>
    public const string NoticePath = "/privacy";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var choice = await dispatcher.SendAsync(new PrivacyNoticeChoiceQuery(), cancellationToken);
        return View(new EditPrivacyNoticeViewModel(new SavePrivacyNoticeCommand(choice.UseOwnText, choice.OwnText), TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SavePrivacyNoticeCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        switch (await dispatcher.SendAsync(form, cancellationToken))
        {
            case PrivacyNoticeOutcome.Saved:
                SetFlash(localizer["Settings.Privacy.Saved"]);
                return RedirectToAction(nameof(Index));
            case PrivacyNoticeOutcome.TooLong:
                ModelState.AddModelError(nameof(SavePrivacyNoticeCommand.OwnText), localizer.Format("Settings.Privacy.Validation.TooLong", PrivacyNoticeRules.OwnTextMaxLength));
                break;
            default:
                ModelState.AddModelError(nameof(SavePrivacyNoticeCommand.OwnText), localizer["Settings.Privacy.Validation.TextRequired"]);
                break;
        }

        return View(new EditPrivacyNoticeViewModel(form, null));
    }
}

public sealed record EditPrivacyNoticeViewModel(SavePrivacyNoticeCommand Form, string? Flash);