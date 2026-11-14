using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Localization;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Public, anonymous start of the booking flow at <c>/book</c> on the salon's host (ADR 0014): one tappable card per
/// active service or group of variants, under its category, each leading to the time picker. A salon with more than a
/// handful of services also gets category tiles, a search and pages (<c>?category=&amp;q=&amp;page=</c>, ADR 0032); an
/// address that cannot be read shows the whole first page. The platform domain has no booking page.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class ChooseServiceController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup, ILocalizationService localizer) : SalonControllerBase
{
    public const string CategoryParameter = "category";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? category, string? q, int? page, CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var choice = await dispatcher.SendAsync(new ChooseServiceQuery(Guid.TryParse(category, out var id) ? id : null, q, page), cancellationToken);

        return View(new ChooseServiceViewModel(tenant?.Name ?? string.Empty, choice, Filter(choice), Search(choice), Pager(choice)));
    }

    private static string ListAddress(Guid? categoryId, string? search, int page = 1)
        => ListUrl.Build(
            PublicBookingDefaults.BookPath,
            (CategoryParameter, categoryId?.ToString()),
            (ListUrl.SearchParameter, search),
            (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));

    /// <summary>"All" and one tile per category in use; choosing a category keeps the search and starts at page one.</summary>
    private FilterTilesModel Filter(ServiceChoice choice)
    {
        var tiles = new List<FilterTile> { new("all", localizer["Booking.Public.AllCategories"], ListAddress(null, choice.Search), choice.CategoryId is null) };
        tiles.AddRange(choice.Categories.Select(c => new FilterTile(c.Id.ToString(), c.Name, ListAddress(c.Id, choice.Search), c.Id == choice.CategoryId, c.Accent)));
        return new FilterTilesModel(localizer["Booking.Public.Categories"], tiles);
    }

    private SearchBoxModel Search(ServiceChoice choice)
        => new(
            PublicBookingDefaults.BookPath,
            choice.Search,
            localizer["Booking.Public.SearchServices"],
            [(CategoryParameter, choice.CategoryId?.ToString())],
            ListAddress(choice.CategoryId, null));

    private static PagerModel Pager(ServiceChoice choice)
        => PagerModel.For(choice.Entries, page => ListAddress(choice.CategoryId, choice.Search, page));
}

/// <param name="Filter">The category tiles, shown when <see cref="ServiceChoice.ShowFilters"/>.</param>
public sealed record ChooseServiceViewModel(string SalonName, ServiceChoice Choice, FilterTilesModel Filter, SearchBoxModel Search, PagerModel Pager)
{
    /// <summary>Headings appear only once the salon has filed at least one service under a category.</summary>
    public bool ShowHeadings => Choice.Categories.Count > 0;

    /// <summary>The entries of the page under their category, in order; a category split by a page break gets its heading again.</summary>
    public IEnumerable<IGrouping<string?, BookableEntry>> Groups => Choice.Entries.Items.GroupBy(e => e.CategoryName);
}