using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.Denied;

/// <summary>Where the cookie handler sends a signed-in user who lacks a permission; the page itself answers 403.</summary>
[RequireTenant]
[Route("account")]
public sealed class DeniedController : SalonControllerBase
{
    [HttpGet("denied")]
    public IActionResult Denied()
    {
        var view = View();
        view.StatusCode = StatusCodes.Status403Forbidden;
        return view;
    }
}