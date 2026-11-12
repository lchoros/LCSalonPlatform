using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Data;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>Reads through the tenant filter only; inactive services stay out of the public list. Grouping happens in memory (<see cref="ServiceGrouping"/>).</summary>
public sealed class ListServicesHandler(SalonDbContext db) : IRequestHandler<ListServicesQuery, IReadOnlyList<ServiceListGroup>>
{
    public async Task<IReadOnlyList<ServiceListGroup>> HandleAsync(ListServicesQuery request, CancellationToken cancellationToken)
    {
        var services = await db.Services.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);

        return ServiceGrouping.Group(services, categories)
            .Select(g => new ServiceListGroup(
                g.Category?.Name,
                g.Services.Select(s => new ServiceListItem(s.Id, s.Name, s.Description, s.Duration, new Money(s.Price, s.Currency))).ToList()))
            .ToList();
    }
}