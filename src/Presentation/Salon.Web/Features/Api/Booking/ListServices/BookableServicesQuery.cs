using Microsoft.EntityFrameworkCore;
using Salon.Api.Contracts.Booking;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Catalog;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Features.Api.Booking.ListServices;

/// <summary>
/// GET services of the booking API (ADR 0097): the salon and every active service, in the catalogue's own order, with
/// the price as <c>/book</c> shows it. No paging and no filter: the app has the whole list and searches on the phone. A query.
/// </summary>
public sealed record BookableServicesQuery : IQuery<ServiceList>;

/// <summary>Reads through the tenant filter only, like the booking page: another salon's services are not there to find.</summary>
public sealed class BookableServicesHandler(
    SalonDbContext db,
    IServiceOfferingResolver offerings,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IVisitBasket basket) : IRequestHandler<BookableServicesQuery, ServiceList>
{
    public async Task<ServiceList> HandleAsync(BookableServicesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(BookableServicesQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken) ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var services = await db.Services.AsNoTracking().Where(s => s.IsActive).ToListAsync(cancellationToken);
        var categories = await db.ServiceCategories.AsNoTracking().ToListAsync(cancellationToken);

        // What the people who offer a service charge for it (ADR 0018): one price when they agree, "from" the cheapest when not.
        var spans = await offerings.PriceSpansAsync(services.Select(s => s.Id).ToList(), cancellationToken);
        return ServiceListMapper.Map(tenant, basket.IsOpen, ServiceCatalogue.Entries(services, categories), spans);
    }
}