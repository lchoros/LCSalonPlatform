using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.EraseClient;

/// <summary>What erasing a client would remove and keep, and what stands in the way (ADR 0062); null for no such client.</summary>
public sealed record ErasurePreviewQuery(Guid ClientId) : IQuery<ErasurePreview?>;

/// <summary>
/// Erases a client at their request (ADR 0062). A command: every table changes or none does. Not undone by the
/// system.
/// </summary>
public sealed record EraseClientCommand(Guid ClientId, Guid? ByUserId) : ICommand<ErasureResult>;

public sealed class EraseClientHandler(IClientDataService data) :
    IRequestHandler<ErasurePreviewQuery, ErasurePreview?>,
    IRequestHandler<EraseClientCommand, ErasureResult>
{
    public Task<ErasurePreview?> HandleAsync(ErasurePreviewQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return data.PreviewErasureAsync(request.ClientId, cancellationToken);
    }

    public Task<ErasureResult> HandleAsync(EraseClientCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return data.EraseAsync(request.ClientId, request.ByUserId, cancellationToken);
    }
}

/// <summary>
/// <c>/admin/clients/{clientId}/erase</c> (ADR 0062): the client asked to be forgotten. The page says what goes and
/// what stays, and what must be settled first; the owner ticks that it cannot be undone and confirms. Owner only
/// (<see cref="Permissions.SettingsManage"/> with <see cref="Permissions.ClientsManage"/>): it cannot be taken back.
/// Another salon's client is a 404.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsManage)]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/clients/{clientId:guid}/erase")]
public sealed class EraseClientController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The client's card; the ClientCard slice owns the route.</summary>
    public static string CardPath(Guid clientId) => $"/admin/clients/{clientId}";

    [HttpGet("")]
    public async Task<IActionResult> Confirm(Guid clientId, CancellationToken cancellationToken)
    {
        var preview = await dispatcher.SendAsync(new ErasurePreviewQuery(clientId), cancellationToken);
        return preview is null ? NotFound() : View(new EraseClientViewModel(preview, null));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Confirm(Guid clientId, bool understood, CancellationToken cancellationToken)
    {
        var preview = await dispatcher.SendAsync(new ErasurePreviewQuery(clientId), cancellationToken);
        if (preview is null)
        {
            return NotFound();
        }

        if (!understood)
        {
            return View(new EraseClientViewModel(preview, localizer["Clients.Erase.Validation.Understood"]));
        }

        var result = await dispatcher.SendAsync(new EraseClientCommand(clientId, CurrentUserId()), cancellationToken);
        switch (result.Outcome)
        {
            case ErasureOutcome.Erased:
                SetFlash(localizer["Clients.Erase.Done"]);
                return Redirect(CardPath(clientId));
            case ErasureOutcome.NotFound:
                return NotFound();
            default:
                // Something changed between the page and the click: show the page again with what stands in the way.
                return View(new EraseClientViewModel(preview with { Blockers = result.Blockers }, null));
        }
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Problem">Why the post was not carried out, already localized.</param>
public sealed record EraseClientViewModel(ErasurePreview Preview, string? Problem)
{
    public string CardPath => EraseClientController.CardPath(Preview.ClientId);

    /// <summary>The resource key that says what to do about a blocker.</summary>
    public static string BlockerKey(ErasureBlocker blocker) => $"Clients.Erase.Blocker.{blocker}";
}