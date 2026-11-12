using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Data;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>Reads through the tenant filter only; inactive services are not bookable and stay out. Grouping happens in memory (<see cref="ServiceGrouping"/>).</summary>
public sealed class ChooseServiceHandler(SalonDbContext db) : IRequestHandler<ChooseServiceQuery, IReadOnlyList<BookableServiceGroup>>
{
    public async Task<IReadOnlyList<BookableServiceGroup>> HandleAsync(ChooseServiceQuery request, CancellationToken cancellationToken)
    {
        var services = await db.Services.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);

        return ServiceGrouping.Group(services, categories)
            .Select(g => new BookableServiceGroup(
                g.Category?.Name,
                g.Services.Select(s => new BookableService(s.Id, s.Name, s.Description, s.Duration, new Money(s.Price, s.Currency))).ToList()))
            .ToList();
    }
}