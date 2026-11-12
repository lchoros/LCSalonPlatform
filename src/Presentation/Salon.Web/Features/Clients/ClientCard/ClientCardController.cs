using Microsoft.AspNetCore.Mvc;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Clients;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

using Card = Salon.Services.Clients.ClientCard;

namespace Salon.Web.Features.Clients.ClientCard;

/// <summary>
/// The client card at <c>/admin/clients/{id}</c>: contact details, notes, the encrypted health section, consent status and
/// the visit history with its counters (ADR 0017). Tenant only, for signed-in users with <see cref="Permissions.ClientsView"/>
/// (ADR 0009, ADR 0011). Another salon's client id is a 404, not a 403: the tenant filter makes it not exist here. A card
/// merged into another shows a banner with a link to the kept card instead of the merge action.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/clients")]
public sealed class ClientCardController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Card(Guid id, CancellationToken cancellationToken)
    {
        var details = await dispatcher.SendAsync(new ClientCardQuery(id), cancellationToken);
        if (details is null)
        {
            return NotFound();
        }

        // [RequireTenant] has already turned a tenant-less request into a 404; instants are shown in the salon's zone (rule 5).
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var zone = (tenant is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId)) ?? DateTimeZone.Utc;

        return View(new ClientCardViewModel(details.Card, details.History, details.MergedIntoName, zone, TakeFlash()));
    }
}

/// <param name="Card">The decrypted card.</param>
/// <param name="History">Upcoming and past visits, starts already in the salon's zone, plus the counters.</param>
/// <param name="MergedIntoName">Name of the kept card when this one was merged away; null otherwise.</param>
/// <param name="Zone">The salon's time zone, for the consent and "client since" dates.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ClientCardViewModel(Card Card, ClientHistory History, string? MergedIntoName, DateTimeZone Zone, string? Flash);