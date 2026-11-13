using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.StaffOverrides;

/// <summary>
/// Staff prices and durations for one service at <c>/admin/services/{id}/staff</c>, for signed-in users with
/// <see cref="Permissions.CatalogManage"/> (ADR 0009, 0018). GET shows a row per bookable staff member; a valid POST saves
/// every row in one transaction and lands on the admin list with a confirmation; an invalid one re-renders with the
/// pipeline's failures next to their fields. An id the salon does not own is a 404 on both verbs.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services")]
public sealed class StaffOverridesController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private const string ListPath = "/admin/services";

    [HttpGet("{id:guid}/staff")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var data = await dispatcher.SendAsync(new GetStaffOverridesQuery(id), cancellationToken);
        return data is null ? NotFound() : View(StaffOverridesForm.From(data));
    }

    [HttpPost("{id:guid}/staff")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, StaffOverridesForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        form.ServiceId = id;

        // A value that did not even bind (letters in a price) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return await RenderAgainAsync(form, cancellationToken);
        }

        StaffOverridesData? data;
        try
        {
            if (!await dispatcher.SendAsync(form.ToCommand(), cancellationToken))
            {
                return NotFound();
            }

            data = await dispatcher.SendAsync(new GetStaffOverridesQuery(id), cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderAgainAsync(form, cancellationToken);
        }

        SetFlash(localizer.Format("Catalog.StaffOverrides.Saved", data?.Service.Name ?? form.ServiceName));
        return Redirect(ListPath);
    }

    /// <summary>The names are not posted, so a re-render reads them again; a service removed meanwhile is a 404 like on GET.</summary>
    private async Task<IActionResult> RenderAgainAsync(StaffOverridesForm form, CancellationToken cancellationToken)
    {
        var data = await dispatcher.SendAsync(new GetStaffOverridesQuery(form.ServiceId), cancellationToken);
        if (data is null)
        {
            return NotFound();
        }

        form.Describe(data);
        return View(form);
    }
}