using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>The active categories a new service may be filed under, in display order. A query: no transaction, tenant required.</summary>
public sealed record ListCategoryOptionsQuery : IQuery<IReadOnlyList<ServiceCategory>>;

/// <summary>Reads through the tenant filter only.</summary>
public sealed class ListCategoryOptionsHandler(SalonDbContext db) : IRequestHandler<ListCategoryOptionsQuery, IReadOnlyList<ServiceCategory>>
{
    public async Task<IReadOnlyList<ServiceCategory>> HandleAsync(ListCategoryOptionsQuery request, CancellationToken cancellationToken)
        => await db.ServiceCategories
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
}