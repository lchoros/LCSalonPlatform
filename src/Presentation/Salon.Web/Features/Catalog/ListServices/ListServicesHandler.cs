using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Data;

namespace Salon.Web.Features.Catalog.ListServices;

/// <summary>Reads through the tenant filter only. Two queries (services, categories) and an in-memory group: a salon has tens of services, not thousands.</summary>
public sealed class ListServicesHandler(SalonDbContext db) : IRequestHandler<ListServicesQuery, IReadOnlyList<ServiceGroupItem>>
{
    public async Task<IReadOnlyList<ServiceGroupItem>> HandleAsync(ListServicesQuery request, CancellationToken cancellationToken)
    {
        var services = await db.Services.AsNoTracking().ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);

        return ServiceGrouping.Group(services, categories)
            .Select(g => new ServiceGroupItem(
                g.Category?.Name,
                g.Category?.IsActive ?? true,
                g.Services.Select(s => new ServiceListItem(s.Id, s.Name, s.Duration, s.BufferAfterMinutes, s.ProcessingMinutes, new Money(s.Price, s.Currency), s.IsActive)).ToList()))
            .ToList();
    }
}