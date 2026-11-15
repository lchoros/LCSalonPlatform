using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Paging;
using Salon.Core.Products;
using Salon.Data;

namespace Salon.Web.Features.Products.ListProducts;

/// <summary>
/// Reads through the tenant filter only. One query, then filtering, ordering and paging in memory with
/// <see cref="ProductRules"/> and <see cref="StockRules"/>: a salon has tens or a few hundred products.
/// </summary>
public sealed class ListProductsHandler(SalonDbContext db) : IRequestHandler<ListProductsQuery, ProductListPage>
{
    public async Task<ProductListPage> HandleAsync(ListProductsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var products = await db.Products.AsNoTracking().ToListAsync(cancellationToken);

        var show = ProductFilterSegments.Parse(request.Show);
        var words = ProductRules.Words(request.Search);
        var rows = products
            .Where(p => show switch
            {
                ProductFilter.NotInUse => !p.IsActive,
                ProductFilter.Low => IsLow(p),
                _ => p.IsActive,
            })
            .Where(p => ProductRules.Matches(p, words))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id)
            .Select(p => new ProductListItem(p.Id, p.Name, p.Brand, p.Price, p.StockOnHand, IsLow(p), p.IsActive))
            .ToList();

        return new ProductListPage(
            Pages.Cut(rows, request.Page, ProductListPage.PageSize),
            show,
            words.Count == 0 ? null : string.Join(' ', words),
            products.Count,
            products.Count(IsLow),
            products.Count(p => !p.IsActive));
    }

    /// <summary>A product nobody uses any more is not "low": there is nothing to reorder.</summary>
    private static bool IsLow(Product product) => product.IsActive && StockRules.IsLow(product.StockOnHand, product.LowStockAt);
}