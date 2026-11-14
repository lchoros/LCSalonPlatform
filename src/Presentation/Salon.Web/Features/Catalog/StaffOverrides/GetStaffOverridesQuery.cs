using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Data;

namespace Salon.Web.Features.Catalog.StaffOverrides;

/// <summary>One service with a row per active, bookable staff member and that person's override, if any; null when the salon has no such service.</summary>
public sealed record GetStaffOverridesQuery(Guid ServiceId) : IQuery<StaffOverridesData?>;

/// <param name="Rows">Active, bookable staff in display order; people without a row carry the defaults (nulls, Offers true).</param>
public sealed record StaffOverridesData(Service Service, IReadOnlyList<StaffOverrideRow> Rows);

/// <param name="DurationMinutes">Null means the service duration.</param>
/// <param name="Price">Null means the service price.</param>
public sealed record StaffOverrideRow(Guid StaffMemberId, string DisplayName, bool Offers, int? DurationMinutes, decimal? Price);

/// <summary>Reads through the tenant filter only: another salon's id is simply not found.</summary>
public sealed class GetStaffOverridesHandler(SalonDbContext db) : IRequestHandler<GetStaffOverridesQuery, StaffOverridesData?>
{
    public async Task<StaffOverridesData?> HandleAsync(GetStaffOverridesQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.ServiceId, cancellationToken);
        if (service is null)
        {
            return null;
        }

        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.DisplayName })
            .ToListAsync(cancellationToken);
        var overrides = await db.StaffServiceOverrides.AsNoTracking()
            .Where(o => o.ServiceId == service.Id)
            .ToDictionaryAsync(o => o.StaffMemberId, cancellationToken);

        var rows = staff
            .Select(s =>
            {
                var row = overrides.GetValueOrDefault(s.Id);
                return new StaffOverrideRow(
                    s.Id,
                    s.DisplayName,
                    ServiceRules.IsOffered(row),
                    row?.Duration is { } duration ? (int)duration.TotalMinutes : null,
                    row?.Price);
            })
            .ToList();

        return new StaffOverridesData(service, rows);
    }
}