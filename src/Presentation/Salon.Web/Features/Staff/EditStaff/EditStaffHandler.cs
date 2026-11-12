using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Web.Features.Staff.EditStaff;

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the staff row commits together or not at all.
/// </summary>
public sealed class EditStaffHandler(
    SalonDbContext db,
    ITenantContext tenantContext) : IRequestHandler<EditStaffCommand, bool>
{
    public async Task<bool> HandleAsync(EditStaffCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(EditStaffCommand)} requires a tenant in scope.");

        var staff = await db.StaffMembers.FirstOrDefaultAsync(s => s.Id == request.Id && s.TenantId == tenantId, cancellationToken);
        if (staff is null)
        {
            return false;
        }

        staff.DisplayName = request.DisplayName.Trim();
        staff.Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        staff.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        staff.Colour = request.Colour.ToUpperInvariant();
        staff.IsBookable = request.IsBookable;
        staff.IsActive = request.IsActive;

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}