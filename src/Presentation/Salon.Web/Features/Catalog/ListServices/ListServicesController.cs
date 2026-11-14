using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Catalog.ListServices;

/// <summary>
/// Admin catalogue at <c>/admin/services</c> for signed-in users with <see cref="Permissions.CatalogManage"/> (ADR 0009,
/// 0013): the services under their category with duration, buffer, price and active flag, links to add and edit, and a
/// link to the categories screen. Narrowed by <c>?category=&amp;q=</c> and paged with <c>?page=</c> through the shared
/// list components (ADR 0032). Also the landing page after the forms save, so it shows the flash they leave.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.CatalogManage)]
[Route("admin/services")]
public sealed class ListServicesController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/services";
    public const string CategoryParameter = "category";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? category, string? q, int? page, CancellationToken cancellationToken)
    {
        var list = await dispatcher.SendAsync(new ListServicesQuery(Guid.TryParse(category, out var id) ? id : null, q, page), cancellationToken);

        // A category split by a page break gets its heading again on the next page.
        var groups = list.Services.Items
            .GroupBy(s => (s.CategoryName, s.CategoryIsActive))
            .Select(g => new ServiceGroupItem(g.Key.CategoryName, g.Key.CategoryIsActive, g.ToList()))
            .ToList();

        var tiles = new List<FilterTile> { new("all", localizer["Catalog.List.AllCategories"], ListAddress(null, list.Search), list.CategoryId is null) };
        tiles.AddRange(list.Categories.Select(c => new FilterTile(c.Id.ToString(), c.Name, ListAddress(c.Id, list.Search), c.Id == list.CategoryId)));

        return View(new ListServicesViewModel(
            list,
            groups,
            new FilterTilesModel(localizer["Catalog.List.FilterLabel"], tiles),
            new SearchBoxModel(Path, list.Search, localizer["Catalog.List.Search"], [(CategoryParameter, list.CategoryId?.ToString())], ListAddress(list.CategoryId, null)),
            PagerModel.For(list.Services, number => ListAddress(list.CategoryId, list.Search, number)),
            TakeFlash()));
    }

    private static string ListAddress(Guid? categoryId, string? search, int page = 1)
        => ListUrl.Build(
            Path,
            (CategoryParameter, categoryId?.ToString()),
            (ListUrl.SearchParameter, search),
            (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));
}