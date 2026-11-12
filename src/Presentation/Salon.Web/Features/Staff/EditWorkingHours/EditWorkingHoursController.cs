using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.EditWorkingHours;

/// <summary>
/// Weekly rota form at <c>/admin/staff/{id}/hours</c> for signed-in users with <see cref="Permissions.StaffManage"/>
/// (ADR 0009, 0010). GET shows the seven days with the current ranges; a valid POST replaces the rota and lands on the
/// staff list with a confirmation; an invalid one re-renders with the pipeline's failures next to their fields. An id the
/// salon does not own is a 404 on both verbs.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff")]
public sealed class EditWorkingHoursController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private const string ListPath = "/admin/staff";

    [HttpGet("{id:guid}/hours")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var rota = await dispatcher.SendAsync(new GetWorkingHoursQuery(id), cancellationToken);
        return rota is null ? NotFound() : View(EditWorkingHoursForm.From(rota));
    }

    [HttpPost("{id:guid}/hours")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditWorkingHoursForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.StaffMemberId = id;

        // A value that did not even bind (an unknown day name) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return await RenderAgainAsync(form, cancellationToken);
        }

        try
        {
            if (!await dispatcher.SendAsync(form.ToCommand(), cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderAgainAsync(form, cancellationToken);
        }

        SetFlash(localizer["Staff.Hours.Saved"]);
        return Redirect(ListPath);
    }

    /// <summary>The name is not posted, so a re-render reads it again; a person removed meanwhile is a 404 like on GET.</summary>
    private async Task<IActionResult> RenderAgainAsync(EditWorkingHoursForm form, CancellationToken cancellationToken)
    {
        var rota = await dispatcher.SendAsync(new GetWorkingHoursQuery(form.StaffMemberId), cancellationToken);
        if (rota is null)
        {
            return NotFound();
        }

        form.DisplayName = rota.DisplayName;
        form.PadForRender();
        return View(form);
    }
}