using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Data;

namespace Salon.Web.Features.Staff.EditWorkingHours;

/// <summary>
/// Replace, not merge: every existing row of the person goes, then the posted ranges come in, both inside the pipeline's
/// transaction (ADR 0003). Two SaveChanges calls keep the deletes ahead of the inserts, so the unique index on
/// (tenant, staff, day, start) never sees the old and the new "09:00" at once. Days marked off and blank slots add nothing.
/// </summary>
public sealed class EditWorkingHoursHandler(SalonDbContext db) : IRequestHandler<EditWorkingHoursCommand, bool>
{
    public async Task<bool> HandleAsync(EditWorkingHoursCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Through the tenant filter: another salon's staff id is "not found", never "updated".
        if (!await db.StaffMembers.AnyAsync(s => s.Id == request.StaffMemberId, cancellationToken))
        {
            return false;
        }

        var existing = await db.WorkingHours.Where(h => h.StaffMemberId == request.StaffMemberId).ToListAsync(cancellationToken);
        db.WorkingHours.RemoveRange(existing);
        await db.SaveChangesAsync(cancellationToken);

        var sortOrder = 0;
        foreach (var day in request.Days.OrderBy(d => d.Day))
        {
            if (day.IsDayOff)
            {
                continue;
            }

            foreach (var range in day.Ranges.Where(r => !r.IsBlank).Select(r => r.Parsed).OfType<TimeRange>().OrderBy(r => r.Start))
            {
                db.WorkingHours.Add(new WorkingHours
                {
                    StaffMemberId = request.StaffMemberId,
                    Day = day.Day,
                    Start = range.Start,
                    End = range.End,
                    SortOrder = ++sortOrder,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}