using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Staff.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>
/// Removes one entry inside the pipeline's transaction (ADR 0003) and publishes <see cref="StaffTimeOffChanged"/> through
/// the outbox (ADR 0007). The entry is looked up by id and person through the tenant filter, so another salon's id (or a
/// guessed one) is simply not found.
/// </summary>
public sealed class DeleteTimeOffHandler(SalonDbContext db, ITenantContext tenantContext, IEventPublisher publisher) : IRequestHandler<DeleteTimeOffCommand, bool>
{
    public async Task<bool> HandleAsync(DeleteTimeOffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(DeleteTimeOffCommand)} requires a tenant in scope.");

        var entry = await db.StaffTimeOff.SingleOrDefaultAsync(t => t.Id == request.EntryId && t.StaffMemberId == request.StaffMemberId, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        db.StaffTimeOff.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new StaffTimeOffChanged(entry.StaffMemberId, tenantId), cancellationToken);

        return true;
    }
}