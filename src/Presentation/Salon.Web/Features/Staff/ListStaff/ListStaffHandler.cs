using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Data;
using Salon.Services.Staff;
using Salon.Web.Framework.Ui;

namespace Salon.Web.Features.Staff.ListStaff;

/// <summary>
/// Reads through the tenant filter only. Three queries (people, every rota row of the salon, who has a picture — never
/// the picture itself, ADR 0088) and an in-memory group: a salon has tens of staff, not thousands.
/// </summary>
public sealed class ListStaffHandler(SalonDbContext db, IStaffPhotoService photos) : IRequestHandler<ListStaffQuery, IReadOnlyList<StaffListItem>>
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
        var photoVersions = await photos.VersionsAsync(cancellationToken);

        return staff
            .Select(s => new StaffListItem(s.Id, s.DisplayName, s.Colour, s.IsActive, s.IsBookable, WorkingHoursSummary.Build(hoursByStaff[s.Id]), StaffPhotoAddress.For(s.Id, photoVersions)))
            .ToList();
    }
}