using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Data;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>Reads through the tenant filter only; inactive services are not bookable and stay out.</summary>
public sealed class ChooseServiceHandler(SalonDbContext db) : IRequestHandler<ChooseServiceQuery, IReadOnlyList<BookableService>>
{
    public async Task<IReadOnlyList<BookableService>> HandleAsync(ChooseServiceQuery request, CancellationToken cancellationToken)
    {
        var rows = await db.Services
            .AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.Description, s.Duration, s.Price, s.Currency })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new BookableService(r.Id, r.Name, r.Description, r.Duration, new Money(r.Price, r.Currency))).ToList();
    }
}
