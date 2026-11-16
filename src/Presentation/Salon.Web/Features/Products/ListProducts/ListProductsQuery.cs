using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Paging;
using Salon.Web.Framework.Lists;

namespace Salon.Web.Features.Products.ListProducts;

/// <summary>
/// The products of the current salon by name, narrowed by a filter and a search and cut into pages (ADR 0036, lists as
/// in ADR 0032). A query: no transaction, tenant required.
/// </summary>
/// <param name="Show">Which products; an unknown value shows the products in use.</param>
/// <param name="Search">Words that must all occur in a product's name, brand or code.</param>
/// <param name="Page">1-based; out of range moves to the nearest page.</param>
public sealed record ListProductsQuery(string? Show = null, string? Search = null, int? Page = null) : IQuery<ProductListPage>;

public enum ProductFilter
{
    /// <summary>Every product the salon still uses.</summary>
    InUse,

    /// <summary>Products in use whose shelf is at or under their threshold.</summary>
    Low,

    /// <summary>Products the salon stopped using; kept for their history.</summary>
    NotInUse,
}

/// <summary>The filter as it is written in an address.</summary>
public static class ProductFilterSegments
{
    public const string Low = "low";
    public const string NotInUse = "not-in-use";

    public static ProductFilter Parse(string? show) => show switch
    {
        Low => ProductFilter.Low,
        NotInUse => ProductFilter.NotInUse,
        _ => ProductFilter.InUse,
    };

    /// <summary>Null for the default, so the plain address stays plain.</summary>
    public static string? Segment(ProductFilter filter) => filter switch
    {
        ProductFilter.Low => Low,
        ProductFilter.NotInUse => NotInUse,
        _ => null,
    };
}

/// <param name="Products">The page of products.</param>
/// <param name="Show">The filter as it was applied.</param>
/// <param name="Search">The search as it was applied; null without one.</param>
/// <param name="Total">Products before any filter, in use or not; 0 shows the empty state.</param>
/// <param name="LowCount">Products in use that are low, whatever the filter.</param>
/// <param name="NotInUseCount">Products no longer in use, whatever the filter.</param>
public sealed record ProductListPage(Paged<ProductListItem> Products, ProductFilter Show, string? Search, int Total, int LowCount, int NotInUseCount)
{
    public const int PageSize = 24;

    /// <summary>What the shelves hold in money (ADR 0049); null without products, and for people who may not see costs.</summary>
    public ShelfWorth? Worth { get; init; }
}

/// <param name="WithoutCost">Products on the shelves that have no cost yet and so are missing from <paramref name="AtCost"/>.</param>
public sealed record ShelfWorth(Money AtCost, Money AtRetail, int WithoutCost);

/// <param name="Price">What a client pays; null for a product the salon only uses itself.</param>
public sealed record ProductListItem(Guid Id, string Name, string? Brand, Money? Price, int OnHand, bool IsLow, bool IsActive);

/// <param name="CanManage">True when the signed-in user may add and change products.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ListProductsViewModel(ProductListPage Page, FilterTilesModel Filter, SearchBoxModel Search, PagerModel Pager, bool CanManage, string? Flash)
{
    /// <summary>The stock list as a file (ADR 0047): for the people who keep the list, once there is a product to list.</summary>
    public bool CanDownload => CanManage && Page.Total > 0;

    /// <summary>Whoever takes payments may sell a product at the counter without a visit (ADR 0053).</summary>
    public bool CanSell { get; init; }
}