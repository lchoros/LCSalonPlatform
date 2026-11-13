using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Clients;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Clients.SearchClients;

/// <summary>
/// Client search at <c>/admin/clients?q=</c>: the front desk types a name or a few digits of a phone and taps the card.
/// Tenant only, for signed-in users with <see cref="Permissions.ClientsView"/> (ADR 0009, ADR 0011).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/clients")]
public sealed class SearchClientsController(IDispatcher dispatcher) : SalonControllerBase
{
    public const int MaxQueryLength = 100;

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        var query = q?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, MaxQueryLength)] : null;
        var clients = await dispatcher.SendAsync(new SearchClientsQuery(query), cancellationToken);

        return View(new SearchClientsViewModel(query, clients, TakeFlash()));
    }
}

/// <param name="Query">What was searched for, echoed in the box and the empty state; null for the unfiltered list.</param>
/// <param name="Clients">Matches ordered by last name then first name; empty shows the empty state.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record SearchClientsViewModel(string? Query, IReadOnlyList<ClientSummary> Clients, string? Flash);