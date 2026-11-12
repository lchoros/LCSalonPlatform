using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.EditService;

/// <summary>One service with the categories its form may pick from; null when the salon has no such service.</summary>
public sealed record GetServiceQuery(Guid ServiceId) : IQuery<ServiceEditData?>;

/// <param name="Categories">Active categories in display order, plus the service's own category when that one is inactive, so the form never loses it.</param>
public sealed record ServiceEditData(Service Service, IReadOnlyList<ServiceCategory> Categories);

/// <summary>Reads through the tenant filter only: another salon's id is simply not found.</summary>
public sealed class GetServiceHandler(SalonDbContext db) : IRequestHandler<GetServiceQuery, ServiceEditData?>
{
    public async Task<ServiceEditData?> HandleAsync(GetServiceQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);
        if (service is null)
        {
            return null;
        }

        var categories = await db.ServiceCategories
            .AsNoTracking()
            .Where(c => c.IsActive || c.Id == service.CategoryId)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return new ServiceEditData(service, categories);
    }
}