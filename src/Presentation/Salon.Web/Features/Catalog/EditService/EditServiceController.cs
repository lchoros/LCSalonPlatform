using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.EditService;

/// <summary>
/// Edit form at <c>/admin/services/{id}</c> for signed-in users with <see cref="Permissions.CatalogManage"/> (ADR 0009,
/// 0013). GET shows every field of the service; a valid POST saves and lands on the admin list with a confirmation; an
/// invalid one re-renders with the pipeline's failures next to their fields. An id the salon does not own is a 404 on
/// both verbs. The category options ride in <see cref="ViewData"/> under <see cref="CategoriesKey"/>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services")]
public sealed class EditServiceController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string CategoriesKey = "Categories";

    private const string ListPath = "/admin/services";

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var data = await dispatcher.SendAsync(new GetServiceQuery(id), cancellationToken);
        return data is null ? NotFound() : Render(EditServiceCommand.From(data.Service), data.Categories);
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditServiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { ServiceId = id };

        // A value that did not even bind (letters in the price) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return await RenderAgainAsync(command, cancellationToken);
        }

        try
        {
            if (!await dispatcher.SendAsync(command, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderAgainAsync(command, cancellationToken);
        }

        SetFlash(localizer.Format("Catalog.Edit.Saved", ServiceRules.NormalizeName(command.Name)));
        return Redirect(ListPath);
    }

    /// <summary>The category options are not posted, so a re-render reads them again; a service removed meanwhile is a 404 like on GET.</summary>
    private async Task<IActionResult> RenderAgainAsync(EditServiceCommand command, CancellationToken cancellationToken)
    {
        var data = await dispatcher.SendAsync(new GetServiceQuery(command.ServiceId), cancellationToken);
        return data is null ? NotFound() : Render(command, data.Categories);
    }

    private ViewResult Render(EditServiceCommand command, IReadOnlyList<ServiceCategory> categories)
    {
        ViewData[CategoriesKey] = categories;
        return View(command);
    }
}