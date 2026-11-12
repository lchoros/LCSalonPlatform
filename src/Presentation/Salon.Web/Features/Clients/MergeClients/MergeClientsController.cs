using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Clients;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.MergeClients;

/// <summary>
/// Merging a duplicate card (ADR 0017) at <c>/admin/clients/{id}/merge</c>: the card <c>{id}</c> is the one that goes
/// away. GET without a target searches for the card to keep; GET <c>/{targetId}</c> shows both side by side with what will
/// happen; POST <c>/{targetId}</c> merges and lands on the kept card with a confirmation. Tenant only, for signed-in users
/// with <see cref="Permissions.ClientsView"/> and <see cref="Permissions.ClientsMerge"/> (Owner and Manager, ADR 0009).
/// An id the salon does not own is a 404 on every verb; the same id on both sides, or a card already merged, is a 400.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[RequirePermission(Permissions.ClientsMerge)]
[Route("admin/clients/{id:guid}/merge")]
public sealed class MergeClientsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const int MaxQueryLength = 100;

    [HttpGet("")]
    public async Task<IActionResult> Pick(Guid id, string? q, CancellationToken cancellationToken)
    {
        var query = q?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, MaxQueryLength)] : null;
        var candidates = await dispatcher.SendAsync(new MergeCandidatesQuery(id, query), cancellationToken);
        if (candidates is null)
        {
            return NotFound();
        }

        if (candidates.Source.IsMerged)
        {
            return Refused(MergeRefusal.SourceAlreadyMerged);
        }

        return View(new MergeCandidatesViewModel(candidates.Source, query, candidates.Matches));
    }

    [HttpGet("{targetId:guid}")]
    public async Task<IActionResult> Confirm(Guid id, Guid targetId, CancellationToken cancellationToken)
    {
        if (id == targetId)
        {
            return Refused(MergeRefusal.SameClient);
        }

        var preview = await dispatcher.SendAsync(new MergePreviewQuery(id, targetId), cancellationToken);
        if (preview is null)
        {
            return NotFound();
        }

        if (preview.Source.IsMerged || preview.Target.IsMerged)
        {
            return Refused(MergeRefusal.SourceAlreadyMerged);
        }

        return View(preview);
    }

    [HttpPost("{targetId:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Merge(Guid id, Guid targetId, CancellationToken cancellationToken)
    {
        if (id == targetId)
        {
            return Refused(MergeRefusal.SameClient);
        }

        MergeClientsResult? result;
        try
        {
            result = await dispatcher.SendAsync(new MergeClientsCommand(targetId, id, CurrentUserId()), cancellationToken);
        }
        catch (ClientMergeRefusedException ex)
        {
            return Refused(ex.Reason);
        }

        if (result is null)
        {
            return NotFound();
        }

        SetFlash(localizer.Format("Clients.Merge.Done", result.SourceName, result.TargetName));
        return Redirect($"/admin/clients/{targetId}");
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var userId) ? userId : null;

    private BadRequestObjectResult Refused(MergeRefusal reason)
        => BadRequest(localizer[reason == MergeRefusal.SameClient ? "Clients.Merge.Refused.SameClient" : "Clients.Merge.Refused.AlreadyMerged"]);
}

/// <param name="Source">The card that will be merged away.</param>
/// <param name="Query">What was searched for, echoed in the box; null before the first search.</param>
/// <param name="Matches">Candidates to keep, never the source itself nor a card already merged.</param>
public sealed record MergeCandidatesViewModel(Card Source, string? Query, IReadOnlyList<ClientSummary> Matches);
