using Microsoft.EntityFrameworkCore;

using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Data;

namespace Salon.Web.Features.Staff.ListStaff;

/// <summary>Reads through the tenant filter only. Two queries (people, then every rota row of the salon) and an in-memory group: a salon has tens of staff, not thousands.</summary>
public sealed class ListStaffHandler(SalonDbContext db) : IRequestHandler<ListStaffQuery, IReadOnlyList<StaffListItem>>
{
    public async Task<IReadOnlyList<StaffListItem>> HandleAsync(ListStaffQuery request, CancellationToken cancellationToken)
    {
        var staff = await db.StaffMembers
            .AsNoTracking()
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => s.DisplayName)
            .ToListAsync(cancellationToken);

        var hoursByStaff = (await db.WorkingHours.AsNoTracking().ToListAsync(cancellationToken))
            .ToLookup(h => h.StaffMemberId);

        return staff
            .Select(s => new StaffListItem(s.Id, s.DisplayName, s.Colour, s.IsActive, s.IsBookable, WorkingHoursSummary.Build(hoursByStaff[s.Id])))
            .ToList();
    }
}