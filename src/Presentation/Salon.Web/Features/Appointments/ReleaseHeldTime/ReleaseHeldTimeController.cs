using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.ReleaseHeldTime;

/// <summary>
/// The desk lets go of a freed time kept for a waiting client (ADR 0065): it is on the booking pages again and the
/// desk can book anybody into it. Nobody else is told. False when the salon has no such offer still standing.
/// </summary>
/// <param name="ByUserId">The login that lets it go; set by the controller, never from the form.</param>
public sealed record ReleaseHeldTimeCommand(Guid OfferId, Guid? ByUserId) : ICommand<bool>;

public sealed class ReleaseHeldTimeHandler(IWaitlistOfferService offers) : IRequestHandler<ReleaseHeldTimeCommand, bool>
{
    public Task<bool> HandleAsync(ReleaseHeldTimeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return offers.ReleaseAsync(request.OfferId, request.ByUserId, cancellationToken);
    }
}

/// <summary>
/// <c>POST /admin/appointments/waitlist/held/{offerId}/release</c> (ADR 0065). Tenant only, behind the waitlist's own
/// permissions (<see cref="Permissions.BookingsManage"/> and <see cref="Permissions.ClientsView"/>). Goes back to the
/// waitlist with what was done; another salon's offer, a missing one and one already ended are all a 404.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments/waitlist/held/{offerId:guid}/release")]
public sealed class ReleaseHeldTimeController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The desk's waitlist; the Waitlist slice owns the route.</summary>
    public const string WaitlistPath = "/admin/appointments/waitlist";

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(Guid offerId, CancellationToken cancellationToken)
    {
        if (!await dispatcher.SendAsync(new ReleaseHeldTimeCommand(offerId, CurrentUserId()), cancellationToken))
        {
            return NotFound();
        }

        SetFlash(localizer["Appointments.Waitlist.Held.Released"]);
        return Redirect(WaitlistPath);
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}