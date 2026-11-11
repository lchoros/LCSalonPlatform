using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// Admin form at <c>/admin/services/new</c>. Tenant only, behind <see cref="FeaturesOptions.CatalogAdmin"/> until
/// permissions exist. Plain post-redirect-get: a valid post lands on the public list with a confirmation, an invalid
/// one re-renders the form with the pipeline's failures next to their fields.
/// </summary>
[RequireTenant]
[FeatureGate(nameof(FeaturesOptions.CatalogAdmin))]
[Route("admin/services")]
public sealed class CreateServiceController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("new")]
    public IActionResult New() => View();

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CreateServiceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A value that did not even bind (letters in the price) is reported by MVC; the rules themselves run in the pipeline.
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

        SetFlash(localizer.Format("Catalog.CreateService.Created", command.Name.Trim()));
        return Redirect("/services");
    }
}