using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Products;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Products;

namespace Salon.Web.Features.Products.ProductCard;

/// <summary>
/// A product's page at <c>/admin/products/{id}?page=</c> (ADR 0036): what is on the shelf, the forms that change it
/// and the history of every change. Tenant only, for everybody who works the diary
/// (<see cref="Permissions.BookingsManage"/>). The forms post to the RecordStock slice; counting the shelf, writing
/// units off and changing the product are shown only to people with <see cref="Permissions.CatalogManage"/>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[Route("admin/products")]
public sealed class ProductCardController(IDispatcher dispatcher) : SalonControllerBase
{
    public const string ListPath = "/admin/products";

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, int? page, CancellationToken cancellationToken)
    {
        var product = await dispatcher.SendAsync(new ProductCardQuery(id, page), cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        return View(new ProductCardViewModel(
            product,
            PagerModel.For(product.History, number => PageAddress(id, number)),
            User.HasClaim(UserClaimTypes.Permission, Permissions.CatalogManage),
            TakeProblem(),
            TakeFlash()));
    }

    public static string PageAddress(Guid id, int page)
        => ListUrl.Build($"{ListPath}/{id}", (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));

    /// <summary>The product form; the SaveProduct slice owns the route.</summary>
    public static string EditPath(Guid id) => $"{ListPath}/{id}/edit";

    /// <summary>Where a stock form posts; the RecordStock slice owns the route.</summary>
    public static string StockPath(Guid id, StockMovementKind kind) => $"{ListPath}/{id}/stock/{StockKindSegments.Segment(kind)}";
}