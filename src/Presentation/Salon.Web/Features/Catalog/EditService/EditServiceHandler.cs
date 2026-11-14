using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Web.Features.Catalog.EditService;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the updated row and the outbox rows (ADR 0007) commit together or
/// not at all. Every edit publishes <see cref="ServiceUpdated"/>; an edit that takes an active service off the public list
/// also publishes <see cref="ServiceDeactivated"/>. Price currency and sort order are not editable here.
/// </summary>
public sealed class EditServiceHandler(SalonDbContext db, ITenantContext tenantContext, IEventPublisher publisher) : IRequestHandler<EditServiceCommand, bool>
{
    public async Task<bool> HandleAsync(EditServiceCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(EditServiceCommand)} requires a tenant in scope.");

        // Through the tenant filter: another salon's service id is "not found", never "updated".
        var service = await db.Services.SingleOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);
        if (service is null)
        {
            return false;
        }

        var deactivated = service.IsActive && !request.IsActive;

        service.Name = ServiceRules.NormalizeName(request.Name);
        service.Description = ServiceRules.NormalizeDescription(request.Description);
        service.Duration = Duration.FromMinutes(request.DurationMinutes);
        service.Price = request.Price;
        service.DepositPercent = request.DepositPercent;
        service.CategoryId = request.CategoryId;
        service.BufferAfterMinutes = request.BufferAfterMinutes;
        service.ProcessingMinutes = request.ProcessingMinutes;
        service.ProcessingStartsAfterMinutes = ServiceRules.NormalizeProcessingStart(request.ProcessingMinutes, request.ProcessingStartsAfterMinutes);
        service.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new ServiceUpdated(service.Id, tenantId), cancellationToken);
        if (deactivated)
        {
            await publisher.PublishAsync(new ServiceDeactivated(service.Id, tenantId), cancellationToken);
        }

        return true;
    }
}