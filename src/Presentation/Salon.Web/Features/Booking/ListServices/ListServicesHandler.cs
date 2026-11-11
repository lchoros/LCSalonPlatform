using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Data;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>Reads through the tenant filter only; inactive services stay out of the public list.</summary>
public sealed class ListServicesHandler(SalonDbContext db) : IRequestHandler<ListServicesQuery, IReadOnlyList<ServiceListItem>>
{
    public async Task<IReadOnlyList<ServiceListItem>> HandleAsync(ListServicesQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.Services
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Description, s.Duration, s.Price, s.Currency })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new ServiceListItem(r.Id, r.Name, r.Description, r.Duration, new Money(r.Price, r.Currency))).ToList();
    }
}