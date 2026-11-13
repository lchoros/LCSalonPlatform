using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Catalog.Events;
using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Web.Features.Catalog.StaffOverrides;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the override rows and the outbox row (ADR 0007) commit together or
/// not at all. A row set back to the defaults is deleted, a changed one updated in place, a new one inserted; a row for a
/// person the salon cannot book (unknown, inactive, not bookable, another salon's) is ignored. Publishes
/// <see cref="ServiceUpdated"/> because the price list may now read differently.
/// </summary>
public sealed class SaveStaffOverridesHandler(SalonDbContext db, ITenantContext tenantContext, IEventPublisher publisher) : IRequestHandler<SaveStaffOverridesCommand, bool>
{
    public async Task<bool> HandleAsync(SaveStaffOverridesCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(SaveStaffOverridesCommand)} requires a tenant in scope.");

        // Through the tenant filter: another salon's service id is "not found", never "updated".
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);
        if (service is null)
        {
            return false;
        }

        var bookable = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .Select(s => s.Id)
            .ToHashSetAsync(cancellationToken);
        var existing = await db.StaffServiceOverrides
            .Where(o => o.ServiceId == service.Id)
            .ToDictionaryAsync(o => o.StaffMemberId, cancellationToken);

        foreach (var input in request.Rows.Where(r => bookable.Contains(r.StaffMemberId)).DistinctBy(r => r.StaffMemberId))
        {
            var row = existing.GetValueOrDefault(input.StaffMemberId);
            var isDefault = input.Offers && input.DurationMinutes is null && input.Price is null;
            if (isDefault)
            {
                if (row is not null)
                {
                    db.StaffServiceOverrides.Remove(row);
                }

                continue;
            }

            if (row is null)
            {
                row = new StaffServiceOverride { TenantId = tenantId, StaffMemberId = input.StaffMemberId, ServiceId = service.Id };
                db.StaffServiceOverrides.Add(row);
            }

            row.Offers = input.Offers;
            row.Duration = input.DurationMinutes is { } minutes ? Duration.FromMinutes(minutes) : null;
            row.Price = input.Price;
        }

        await db.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new ServiceUpdated(service.Id, tenantId), cancellationToken);
        return true;
    }
}