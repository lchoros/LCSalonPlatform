using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Staff;
using Salon.Core.Staff.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Web.Features.Staff.CreateStaff;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the staff row and the <see cref="StaffMemberCreated"/> outbox row
/// (ADR 0007) commit together or not at all. The new person goes last in the list; their rota starts empty.
/// </summary>
public sealed class CreateStaffHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    IEventPublisher publisher) : IRequestHandler<CreateStaffCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateStaffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(CreateStaffCommand)} requires a tenant in scope.");

        var lastSortOrder = await db.StaffMembers.MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? 0;
        var staff = new StaffMember
        {
            DisplayName = request.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Colour = request.Colour.ToUpperInvariant(),
            IsBookable = request.IsBookable,
            SortOrder = lastSortOrder + 1,
            CreatedAt = clock.GetCurrentInstant(),
        };

        db.StaffMembers.Add(staff);
        await db.SaveChangesAsync(cancellationToken);
        await publisher.PublishAsync(new StaffMemberCreated(staff.Id, tenantId, staff.DisplayName), cancellationToken);

        return staff.Id;
    }
}