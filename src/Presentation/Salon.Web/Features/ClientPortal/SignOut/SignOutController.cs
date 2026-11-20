using Microsoft.AspNetCore.Mvc;
using Salon.Services.Localization;
using Salon.Web.Framework.ClientPortal;
using Salon.Web.Framework.Controllers;

namespace Salon.Web.Features.ClientPortal.SignOut;

/// <summary>
/// <c>POST /my-bookings/sign-out</c> (ADR 0083): the client's cookie goes and the sign-in says so. Only the client's
/// own cookie is touched; a member of staff signed in on the same browser stays signed in.
/// </summary>
[ClientPortalGate]
public sealed class SignOutController(IClientPortalSession session, ILocalizationService localizer) : SalonControllerBase
{
    [HttpPost(ClientPortalDefaults.SignOutPath)]
    [ValidateAntiForgeryToken]
    public IActionResult Leave()
    {
        session.Clear(Response);
        SetFlash(localizer["ClientPortal.SignIn.SignedOut"]);
        return Redirect(ClientPortalDefaults.SignInPath);
    }
}