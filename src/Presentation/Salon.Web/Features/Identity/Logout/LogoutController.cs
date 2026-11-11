using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Identity;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.Logout;

/// <summary>POST-only sign-out (a link could be triggered cross-site); the layout's button posts here with an anti-forgery token.</summary>
[RequireTenant]
[Route("account")]
public sealed class LogoutController : SalonControllerBase
{
    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(SalonAuthenticationDefaults.Scheme);
        return Redirect("/");
    }
}