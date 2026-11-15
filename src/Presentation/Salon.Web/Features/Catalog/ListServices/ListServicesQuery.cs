using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Paging;
using Salon.Web.Framework.Lists;

namespace Salon.Web.Features.Catalog.ListServices;

/// <summary>
/// The services of the current salon (active and inactive) in the catalogue's order, narrowed by category and search
/// and cut into pages (ADR 0032). A query: no transaction, tenant required.
/// </summary>
/// <param name="CategoryId">Only this category; an id the salon does not have is ignored.</param>
/// <param name="Search">Words that must all occur in a service.</param>
/// <param name="Page">1-based; out of range moves to the nearest page.</param>
public sealed record ListServicesQuery(Guid? CategoryId = null, string? Search = null, int? Page = null) : IQuery<ServiceListPage>;

/// <param name="Services">The page of services.</param>
/// <param name="Categories">Categories that hold at least one service, in display order, for the filter.</param>
/// <param name="CategoryId">The category the list is narrowed to, when it is one the salon has.</param>
/// <param name="Search">The search as it was applied; null without one.</param>
/// <param name="TotalServices">Services before any filter; 0 shows the empty state.</param>
public sealed record ServiceListPage(
    Paged<ServiceListItem> Services,
    IReadOnlyList<ServiceCategoryOption> Categories,
    Guid? CategoryId,
    string? Search,
    int TotalServices)
{
    public const int PageSize = 24;
}

public sealed record ServiceCategoryOption(Guid Id, string Name);

/// <param name="CategoryName">Null for the uncategorised group; the view localizes its heading.</param>
/// <param name="CategoryIsActive">False shows a badge next to the heading so the owner knows why the category is missing from the pickers.</param>
public sealed record ServiceGroupItem(string? CategoryName, bool CategoryIsActive, IReadOnlyList<ServiceListItem> Services);

/// <summary>One row of the admin list; the price carries its currency so the view can format it for the reader.</summary>
/// <param name="VariantGroup">The title this service shares with its sibling variants, when it is a variant.</param>
public sealed record ServiceListItem(
    Guid Id,
    string Name,
    Duration Duration,
    int BufferAfterMinutes,
    int ProcessingMinutes,
    Money Price,
    bool IsActive,
    string? CategoryName = null,
    bool CategoryIsActive = true,
    string? VariantGroup = null);

/// <param name="Groups">The services of the page under their category, in display order.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ListServicesViewModel(
    ServiceListPage Page,
    IReadOnlyList<ServiceGroupItem> Groups,
    FilterTilesModel Filter,
    SearchBoxModel Search,
    PagerModel Pager,
    string? Flash);