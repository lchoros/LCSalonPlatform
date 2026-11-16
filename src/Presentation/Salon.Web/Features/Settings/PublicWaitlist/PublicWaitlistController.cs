using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Settings;
using Salon.Services.Localization;
using Salon.Services.Settings;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.PublicWaitlist;

/// <summary>Whether the public booking pages offer "tell me when a time comes up" (ADR 0055).</summary>
public sealed record GetPublicWaitlistQuery : IQuery<SavePublicWaitlistCommand>;

/// <summary>The switch as posted. An unticked box is not posted, which is "off".</summary>
/// <param name="Enabled">Offer the form on the booking pages.</param>
/// <param name="TellWhenFreed">Text the first client waiting when a visit is cancelled (ADR 0056).</param>
public sealed record SavePublicWaitlistCommand(bool Enabled, bool TellWhenFreed = false) : ICommand<bool>;

public sealed class GetPublicWaitlistHandler(ISettingService settings) : IRequestHandler<GetPublicWaitlistQuery, SavePublicWaitlistCommand>
{
    public async Task<SavePublicWaitlistCommand> HandleAsync(GetPublicWaitlistQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rules = await settings.LoadAsync<BookingSettings>(cancellationToken);
        return new SavePublicWaitlistCommand(rules.PublicWaitlist, rules.TellWaitlistWhenFreed);
    }
}

public sealed class SavePublicWaitlistHandler(ISettingService settings) : IRequestHandler<SavePublicWaitlistCommand, bool>
{
    public async Task<bool> HandleAsync(SavePublicWaitlistCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Load first: the booking rules this form does not show must keep their values.
        var current = await settings.LoadAsync<BookingSettings>(cancellationToken);
        current.PublicWaitlist = request.Enabled;
        current.TellWaitlistWhenFreed = request.TellWhenFreed;
        await settings.SaveAsync(current, cancellationToken);
        return true;
    }
}

/// <summary>
/// <c>/admin/settings/waitlist</c> (ADR 0055): the owner switches the public "tell me when a time comes up" form on or
/// off. Off to begin with, because it lets people who are not clients yet leave their name and number. Behind
/// <c>Settings.Manage</c>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/waitlist")]
public sealed class PublicWaitlistController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The desk's waitlist; the Waitlist slice owns the route.</summary>
    public const string ListPath = "/admin/appointments/waitlist";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new PublicWaitlistViewModel(await dispatcher.SendAsync(new GetPublicWaitlistQuery(), cancellationToken), TakeFlash()));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SavePublicWaitlistCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        await dispatcher.SendAsync(form, cancellationToken);
        SetFlash(localizer["Settings.Waitlist.Saved"]);
        return RedirectToAction(nameof(Index));
    }
}

public sealed record PublicWaitlistViewModel(SavePublicWaitlistCommand Form, string? Flash);