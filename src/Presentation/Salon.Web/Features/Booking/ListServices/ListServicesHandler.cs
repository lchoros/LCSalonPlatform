using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Data;
using Salon.Services.Catalog;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>
/// Reads through the tenant filter only; inactive services stay out of the public list. Grouping happens in memory
/// (<see cref="ServiceGrouping"/>); the "from" price comes from the offering resolver when staff charge differently (ADR 0018).
/// </summary>
public sealed class ListServicesHandler(SalonDbContext db, IServiceOfferingResolver offerings) : IRequestHandler<ListServicesQuery, IReadOnlyList<ServiceListGroup>>
{
    public async Task<IReadOnlyList<ServiceListGroup>> HandleAsync(ListServicesQuery request, CancellationToken cancellationToken)
    {
        var services = await db.Services.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);
        var spans = await offerings.PriceSpansAsync(services.Select(s => s.Id).ToList(), cancellationToken);

        return ServiceGrouping.Group(services, categories)
            .Select(g => new ServiceListGroup(
                g.Category?.Name,
                g.Services.Select(s => Item(s, spans.GetValueOrDefault(s.Id, new PriceSpan(s.Price, s.Price)))).ToList()))
            .ToList();
    }

    /// <summary>One price when everyone charges the same (the staff price when they all override it); "from" the cheapest otherwise.</summary>
    private static ServiceListItem Item(Service service, PriceSpan span)
        => new(
            service.Id,
            service.Name,
            service.Description,
            service.Duration,
            new Money(span.Min, service.Currency),
            span.IsRange);
}