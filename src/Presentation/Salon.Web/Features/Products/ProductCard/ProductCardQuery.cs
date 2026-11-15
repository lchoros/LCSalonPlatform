using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Paging;
using Salon.Core.Products;
using Salon.Web.Framework.Lists;

namespace Salon.Web.Features.Products.ProductCard;

/// <summary>
/// One product with what is on its shelf and the history of that shelf, newest first, in pages (ADR 0036). Null when
/// the salon has no such product. A query: no transaction, tenant required.
/// </summary>
/// <param name="Page">1-based page of the history; out of range moves to the nearest page.</param>
public sealed record ProductCardQuery(Guid Id, int? Page = null) : IQuery<ProductCard?>;

/// <param name="Price">What a client pays; null for a product the salon only uses itself.</param>
/// <param name="History">The shelf's lines, newest first.</param>
public sealed record ProductCard(
    Guid Id,
    string Name,
    string? Brand,
    string? Code,
    Money? Price,
    int OnHand,
    int? LowStockAt,
    bool IsLow,
    bool IsActive,
    Paged<StockLine> History)
{
    public const int PageSize = 20;
}

/// <param name="Quantity">Signed: above zero came in, below zero went out.</param>
/// <param name="At">In the salon's zone.</param>
/// <param name="ByName">Who added the line; null when the login is gone or unknown.</param>
public sealed record StockLine(Guid Id, StockMovementKind Kind, int Quantity, int OnHandAfter, string? Note, LocalDateTime At, string? ByName);

/// <param name="CanManage">True when the signed-in user may change the product, count the shelf and write units off.</param>
/// <param name="Problem">Why the last change was refused, already localized; shown above the forms.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ProductCardViewModel(ProductCard Product, PagerModel Pager, bool CanManage, string? Problem, string? Flash);