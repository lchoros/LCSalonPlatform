using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Products.ListProducts;

/// <summary>
/// The salon's shelves at <c>/admin/products?show=&amp;q=&amp;page=</c> (ADR 0036): every product with what is on hand,
/// a mark on the ones running low, search, a filter and pages through the shared list components (ADR 0032). Tenant
/// only, for everybody who works the diary (<see cref="Permissions.BookingsManage"/>): the person at the chair is the
/// one who sees a shelf run empty. Adding and changing products needs <see cref="Permissions.CatalogManage"/>; the
/// page only shows that link to people who have it. Also the landing page after a product is added.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[Route("admin/products")]
public sealed class ListProductsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/products";
    public const string ShowParameter = "show";

    /// <summary>The form that adds a product; the SaveProduct slice owns the route.</summary>
    public const string NewPath = "/admin/products/new";

    /// <summary>The import of a product list from a file; the ImportProducts slice owns the route.</summary>
    public const string ImportPath = "/admin/products/import";

    /// <summary>The product list with today's counts as a CSV file (ADR 0047); the ExportProducts slice owns the route.</summary>
    public const string StockFilePath = "/admin/products/stock.csv";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? show, string? q, int? page, CancellationToken cancellationToken)
    {
        var list = await dispatcher.SendAsync(new ListProductsQuery(show, q, page), cancellationToken);
        var canManage = User.HasClaim(UserClaimTypes.Permission, Permissions.CatalogManage);
        if (!canManage)
        {
            // What the stock cost is the owner's and the manager's to see (ADR 0049).
            list = list with { Worth = null };
        }

        var tiles = new List<FilterTile>
        {
            new("in-use", localizer["Products.List.Filter.InUse"], ListAddress(ProductFilter.InUse, list.Search), list.Show == ProductFilter.InUse),
            new(ProductFilterSegments.Low, localizer.Format("Products.List.Filter.Low", list.LowCount), ListAddress(ProductFilter.Low, list.Search), list.Show == ProductFilter.Low, 1),
        };
        if (list.NotInUseCount > 0 || list.Show == ProductFilter.NotInUse)
        {
            tiles.Add(new(ProductFilterSegments.NotInUse, localizer.Format("Products.List.Filter.NotInUse", list.NotInUseCount), ListAddress(ProductFilter.NotInUse, list.Search), list.Show == ProductFilter.NotInUse, 3));
        }

        return View(new ListProductsViewModel(
            list,
            new FilterTilesModel(localizer["Products.List.FilterLabel"], tiles),
            new SearchBoxModel(Path, list.Search, localizer["Products.List.Search"], [(ShowParameter, ProductFilterSegments.Segment(list.Show))], ListAddress(list.Show, null)),
            PagerModel.For(list.Products, number => ListAddress(list.Show, list.Search, number)),
            canManage,
            TakeFlash()));
    }

    /// <summary>A product's own page; the ProductCard slice owns the route.</summary>
    public static string CardPath(Guid id) => $"{Path}/{id}";

    private static string ListAddress(ProductFilter show, string? search, int page = 1)
        => ListUrl.Build(
            Path,
            (ShowParameter, ProductFilterSegments.Segment(show)),
            (ListUrl.SearchParameter, search),
            (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));
}