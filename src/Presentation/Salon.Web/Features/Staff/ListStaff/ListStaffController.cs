using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.ListStaff;

/// <summary>
/// Admin list at <c>/admin/staff</c> for signed-in users with <see cref="Permissions.StaffManage"/> (ADR 0009, 0010): every
/// staff member with a one-line weekly rota, a link to add a person and one to edit their hours. Also the landing page after
/// those two forms save, so it shows the flash they leave.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff")]
public sealed class ListStaffController(IDispatcher dispatcher) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var staff = await dispatcher.SendAsync(new ListStaffQuery(), cancellationToken);
        return View(new ListStaffViewModel(staff, TakeFlash()));
    }
}