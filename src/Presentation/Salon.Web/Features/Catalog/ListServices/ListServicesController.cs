using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.ListServices;

/// <summary>
/// Admin catalogue at <c>/admin/services</c> for signed-in users with <see cref="Permissions.CatalogManage"/> (ADR 0009,
/// 0013): every service grouped by category with duration, buffer, price and active flag, links to add and edit, and a
/// link to the categories screen. Also the landing page after the forms save, so it shows the flash they leave.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services")]
public sealed class ListServicesController(IDispatcher dispatcher) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var groups = await dispatcher.SendAsync(new ListServicesQuery(), cancellationToken);
        return View(new ListServicesViewModel(groups, TakeFlash()));
    }
}