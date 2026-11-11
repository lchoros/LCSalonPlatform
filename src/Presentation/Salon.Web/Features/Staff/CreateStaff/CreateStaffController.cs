using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.CreateStaff;

/// <summary>
/// Admin form at <c>/admin/staff/new</c> for signed-in users with <see cref="Permissions.StaffManage"/> (ADR 0009, 0010).
/// Plain post-redirect-get: a valid post lands on the staff list with a confirmation, an invalid one re-renders the form
/// with the pipeline's failures next to their fields.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff")]
public sealed class CreateStaffController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private const string ListPath = "/admin/staff";

    [HttpGet("new")]
    public IActionResult New() => View(CreateStaffCommand.Empty);

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CreateStaffCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(command);
        }

        try
        {
            await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(command);
        }

        SetFlash(localizer.Format("Staff.Create.Created", command.DisplayName.Trim()));
        return Redirect(ListPath);
    }
}