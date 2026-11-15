using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Products;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Products.SaveProduct;

/// <summary>
/// The product form at <c>/admin/products/new</c> and <c>/admin/products/{id}/edit</c> (ADR 0036). Tenant only, for
/// signed-in users with <see cref="Permissions.CatalogManage"/> (ADR 0009): what the salon stocks and sells is the
/// owner's and the manager's decision, as the services are. Plain post-redirect-get: a valid post lands on the
/// product's page with a confirmation, an invalid one re-renders the form with the failures next to their fields.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/products")]
public sealed class SaveProductController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The list; the ListProducts slice owns the route.</summary>
    public const string ListPath = "/admin/products";

    [HttpGet("new")]
    public IActionResult New() => View("Form", SaveProductCommand.Empty);

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> New(SaveProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SaveAsync(command with { Id = null }, "Products.Save.Added", cancellationToken);
    }

    [HttpGet("{id:guid}/edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
    {
        var product = await dispatcher.SendAsync(new GetProductForEditQuery(id), cancellationToken);
        return product is null ? NotFound() : View("Form", SaveProductCommand.From(product));
    }

    [HttpPost("{id:guid}/edit")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Edit(Guid id, SaveProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // An opening count belongs to a new product only; an existing shelf changes on its own page.
        return SaveAsync(command with { Id = id, OpeningStock = null }, "Products.Save.Saved", cancellationToken);
    }

    /// <summary>A product's own page; the ProductCard slice owns the route.</summary>
    public static string CardPath(Guid id) => $"{ListPath}/{id}";

    private async Task<IActionResult> SaveAsync(SaveProductCommand command, string flashKey, CancellationToken cancellationToken)
    {
        // A value that did not even bind (letters in the price) is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View("Form", command);
        }

        Guid? saved;
        try
        {
            saved = await dispatcher.SendAsync(command with { ByUserId = CurrentUserId() }, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View("Form", command);
        }

        if (saved is not { } id)
        {
            // Another salon's id looks the same as a missing one (tenant filter).
            return NotFound();
        }

        SetFlash(localizer.Format(flashKey, ProductRules.NormalizeName(command.Name)));
        return Redirect(CardPath(id));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}