using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Data;

namespace Salon.Web.Features.Staff.EditWorkingHours;

/// <summary>One staff member's name and current rota for the hours form; null when the salon has no such person.</summary>
public sealed record GetWorkingHoursQuery(Guid StaffMemberId) : IQuery<StaffRota?>;

public sealed record StaffRota(Guid StaffMemberId, string DisplayName, IReadOnlyList<WorkingHours> Hours);

/// <summary>Reads through the tenant filter only: another salon's id is simply not found.</summary>
public sealed class GetWorkingHoursHandler(SalonDbContext db) : IRequestHandler<GetWorkingHoursQuery, StaffRota?>
{
    public async Task<StaffRota?> HandleAsync(GetWorkingHoursQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var staff = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);
        if (staff is null)
        {
            return null;
        }

        var hours = await db.WorkingHours
            .AsNoTracking()
            .Where(h => h.StaffMemberId == staff.Id)
            .OrderBy(h => h.Day)
            .ThenBy(h => h.Start)
            .ToListAsync(cancellationToken);

        return new StaffRota(staff.Id, staff.DisplayName, hours);
    }
}