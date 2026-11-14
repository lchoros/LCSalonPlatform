using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>
/// Categories screen at <c>/admin/services/categories</c> for signed-in users with <see cref="Permissions.CatalogManage"/>
/// (ADR 0009, 0013): the list with an inline add form, a rename form per row and a deactivate/restore button. Every POST is
/// post-redirect-get back here with a confirmation; a rejected name re-renders the page with the message next to the field
/// that posted it (<see cref="AddErrorKey"/> for the add form, <see cref="RenameErrorKey"/> for the row being renamed).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services/categories")]
public sealed class ManageCategoriesController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string AddErrorKey = "Name";
    public const string RenameErrorKey = "RenameName";

    private const string Path = "/admin/services/categories";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => await RenderAsync(string.Empty, null, null, TakeFlash(), cancellationToken);

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddCategoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors.Select(e => new FluentValidation.Results.ValidationFailure(AddErrorKey, e.ErrorMessage)));
            return await RenderAsync(command.Name ?? string.Empty, null, null, null, cancellationToken);
        }

        SetFlash(localizer.Format("Catalog.Categories.Added", ServiceRules.NormalizeName(command.Name)));
        return Redirect(Path);
    }

    [HttpPost("{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(Guid id, string? name, CancellationToken cancellationToken)
    {
        var command = new RenameCategoryCommand(id, name ?? string.Empty);
        try
        {
            if (!await dispatcher.SendAsync(command, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors.Select(e => new FluentValidation.Results.ValidationFailure(RenameErrorKey, e.ErrorMessage)));
            return await RenderAsync(string.Empty, id, command.Name, null, cancellationToken);
        }

        SetFlash(localizer.Format("Catalog.Categories.Renamed", ServiceRules.NormalizeName(command.Name)));
        return Redirect(Path);
    }

    [HttpPost("{id:guid}/deactivate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
        => SetActiveAsync(id, false, "Catalog.Categories.Deactivated", cancellationToken);

    [HttpPost("{id:guid}/activate")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken)
        => SetActiveAsync(id, true, "Catalog.Categories.Restored", cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, string flashKey, CancellationToken cancellationToken)
    {
        if (!await dispatcher.SendAsync(new SetCategoryActiveCommand(id, isActive), cancellationToken))
        {
            return NotFound();
        }

        SetFlash(localizer[flashKey]);
        return Redirect(Path);
    }

    private async Task<IActionResult> RenderAsync(string newName, Guid? renamingId, string? renamingName, string? flash, CancellationToken cancellationToken)
    {
        var categories = await dispatcher.SendAsync(new ListCategoriesQuery(), cancellationToken);
        return View("Index", new ManageCategoriesViewModel(categories, newName, renamingId, renamingName, flash));
    }
}