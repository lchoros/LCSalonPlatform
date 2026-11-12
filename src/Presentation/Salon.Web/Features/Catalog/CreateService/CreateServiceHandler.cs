using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the service row and the <see cref="ServiceCreated"/> outbox row
/// (ADR 0007) commit together or not at all. The price is in the tenant's currency; the new service goes last in the list.
/// </summary>
public sealed class CreateServiceHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IEventPublisher publisher) : IRequestHandler<CreateServiceCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateServiceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(CreateServiceCommand)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");

        var lastSortOrder = await db.Services.MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0;
        var service = new Service
        {
            Name = ServiceRules.NormalizeName(request.Name),
            Description = ServiceRules.NormalizeDescription(request.Description),
            Duration = Duration.FromMinutes(request.DurationMinutes),
            Price = request.Price,
            Currency = tenant.Currency,
            SortOrder = lastSortOrder + 1,
            CategoryId = request.CategoryId,
            BufferAfterMinutes = request.BufferAfterMinutes,
            ProcessingMinutes = request.ProcessingMinutes,
            ProcessingStartsAfterMinutes = ServiceRules.NormalizeProcessingStart(request.ProcessingMinutes, request.ProcessingStartsAfterMinutes),
        };

        db.Services.Add(service);
        await db.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new ServiceCreated(service.Id, tenantId, service.Name), cancellationToken);

        return service.Id;
    }
}