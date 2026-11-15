using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Paging;
using Salon.Data;

namespace Salon.Web.Features.Catalog.ListServices;

/// <summary>
/// Reads through the tenant filter only. Two queries (services, categories), then filtering, ordering and paging in
/// memory with <see cref="ServiceCatalogue"/>: a salon has tens of services, not thousands.
/// </summary>
public sealed class ListServicesHandler(SalonDbContext db) : IRequestHandler<ListServicesQuery, ServiceListPage>
{
    public async Task<ServiceListPage> HandleAsync(ListServicesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var services = await db.Services.AsNoTracking().ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);

        var inUse = ServiceGrouping.Group(services, categories)
            .Where(g => g.Category is not null)
            .Select(g => new ServiceCategoryOption(g.Category!.Id, g.Category.Name))
            .ToList();
        var categoryId = request.CategoryId is { } wanted && inUse.Any(c => c.Id == wanted) ? wanted : (Guid?)null;
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : string.Join(' ', ServiceCatalogue.Words(request.Search));

        // Variants stay next to each other, in the place of the first of them, exactly as clients see them.
        var rows = ServiceCatalogue.Entries(services, categories, categoryId, search)
            .SelectMany(e => e.Services.Select(s => new ServiceListItem(
                s.Id,
                s.Name,
                s.Duration,
                s.BufferAfterMinutes,
                s.ProcessingMinutes,
                new Money(s.Price, s.Currency),
                s.IsActive,
                e.Category?.Name,
                e.Category?.IsActive ?? true,
                s.VariantGroup)))
            .ToList();

        return new ServiceListPage(Pages.Cut(rows, request.Page, ServiceListPage.PageSize), inUse, categoryId, search, services.Count);
    }
}