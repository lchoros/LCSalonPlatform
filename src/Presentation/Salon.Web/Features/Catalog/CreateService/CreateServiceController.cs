using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// Admin form at <c>/admin/services/new</c>. Tenant only, for signed-in users with <see cref="Permissions.CatalogManage"/>
/// (ADR 0009). Plain post-redirect-get: a valid post lands on the admin list with a confirmation, an invalid one
/// re-renders the form with the pipeline's failures next to their fields. The category options ride in
/// <see cref="ViewData"/> under <see cref="CategoriesKey"/>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services")]
public sealed class CreateServiceController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string CategoriesKey = "Categories";

    private const string ListPath = "/admin/services";

    [HttpGet("new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken) => await RenderAsync(null, cancellationToken);

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CreateServiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A value that did not even bind (letters in the price) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return await RenderAsync(command, cancellationToken);
        }

        try
        {
            await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderAsync(command, cancellationToken);
        }

        SetFlash(localizer.Format("Catalog.CreateService.Created", ServiceRules.NormalizeName(command.Name)));
        return Redirect(ListPath);
    }

    /// <summary>The category options are not posted, so every render reads them; a null model is the empty form.</summary>
    private async Task<IActionResult> RenderAsync(CreateServiceCommand? command, CancellationToken cancellationToken)
    {
        ViewData[CategoriesKey] = await dispatcher.SendAsync(new ListCategoryOptionsQuery(), cancellationToken);
        return View(command);
    }
}