using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Staff.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the time-off row and the <see cref="StaffTimeOffChanged"/> outbox row
/// (ADR 0007) commit together or not at all. The validator has already parsed and checked every field.
/// </summary>
public sealed class AddTimeOffHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    IEventPublisher publisher) : IRequestHandler<AddTimeOffCommand, bool>
{
    public async Task<bool> HandleAsync(AddTimeOffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(AddTimeOffCommand)} requires a tenant in scope.");

        // Through the tenant filter: another salon's staff id is "not found", never "added to".
        if (!await db.StaffMembers.AnyAsync(s => s.Id == request.StaffMemberId, cancellationToken))
        {
            return false;
        }

        var entry = request.ToEntry() ?? throw new InvalidOperationException("The validator must refuse an entry that does not parse.");
        entry.CreatedAt = clock.GetCurrentInstant();

        db.StaffTimeOff.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new StaffTimeOffChanged(entry.StaffMemberId, tenantId), cancellationToken);

        return true;
    }
}