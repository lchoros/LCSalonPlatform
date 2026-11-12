using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>Every category of the current salon (active and inactive) in display order, with how many services each groups. A query: no transaction, tenant required.</summary>
public sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategoryListItem>>;

public sealed record CategoryListItem(Guid Id, string Name, int SortOrder, bool IsActive, int ServiceCount);

/// <param name="Categories">In display order; empty shows the empty state.</param>
/// <param name="NewName">What the add form posted when it is re-rendered with errors; blank otherwise.</param>
/// <param name="RenamingId">The row whose rename failed validation, so its input keeps the rejected text and shows the messages.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ManageCategoriesViewModel(
    IReadOnlyList<CategoryListItem> Categories,
    string NewName,
    Guid? RenamingId,
    string? RenamingName,
    string? Flash);

/// <summary>Reads through the tenant filter only; one query for the categories and one grouped count for the services.</summary>
public sealed class ListCategoriesHandler(SalonDbContext db) : IRequestHandler<ListCategoriesQuery, IReadOnlyList<CategoryListItem>>
{
    public async Task<IReadOnlyList<CategoryListItem>> HandleAsync(ListCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await db.ServiceCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        var counts = await db.Services
            .AsNoTracking()
            .Where(s => s.CategoryId != null)
            .GroupBy(s => s.CategoryId!.Value)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CategoryId, g => g.Count, cancellationToken);

        return categories
            .Select(c => new CategoryListItem(c.Id, c.Name, c.SortOrder, c.IsActive, counts.GetValueOrDefault(c.Id)))
            .ToList();
    }
}