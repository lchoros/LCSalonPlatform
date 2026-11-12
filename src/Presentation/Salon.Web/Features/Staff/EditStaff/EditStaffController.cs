using FluentValidation;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.EditStaff;

/// <summary>
/// Admin form at <c>/admin/staff/{id}/edit</c> for signed-in users with <see cref="Permissions.StaffManage"/> (ADR 0009, 0010).
/// Plain post-redirect-get: a valid post lands on the staff list with a confirmation, an invalid one re-renders the form
/// with the pipeline's failures next to their fields.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff")]
public sealed class EditStaffController(IDispatcher dispatcher, ILocalizationService localizer, SalonDbContext db, ITenantContext tenantContext) : SalonControllerBase
{
    private const string ListPath = "/admin/staff";

    private readonly SalonDbContext db = db;
    private readonly ITenantContext tenantContext = tenantContext;

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var command = await BuildCommandAsync(id, cancellationToken);
        if (command is null)
        {
            return NotFound();
        }

        return View(command);
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditStaffCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(command);
        }

        try
        {
            var success = await dispatcher.SendAsync(command, cancellationToken);
            if (!success)
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(command);
        }

        SetFlash(localizer.Format("Staff.Edit.Saved", command.DisplayName.Trim()));
        return Redirect(ListPath);
    }

    private async Task<EditStaffCommand?> BuildCommandAsync(Guid id, CancellationToken cancellationToken)
    {
        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantContext.TenantId, cancellationToken);
        if (staff is null)
        {
            return null;
        }

        return new EditStaffCommand(staff.Id, staff.DisplayName, staff.Email, staff.Phone, staff.Colour, staff.IsBookable, staff.IsActive);
    }
}