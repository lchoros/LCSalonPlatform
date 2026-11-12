using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>One staff member's name and time-off entries split around the salon's local today; null when the salon has no such person.</summary>
public sealed record GetTimeOffQuery(Guid StaffMemberId) : IQuery<StaffTimeOffPage?>;

/// <param name="Upcoming">Entries that end today or later, soonest first.</param>
/// <param name="Past">The most recent <see cref="PastShown"/> entries that ended before today, newest first.</param>
/// <param name="PastTotal">How many past entries exist in total, so the page can say "and 12 more".</param>
public sealed record StaffTimeOffPage(Guid StaffMemberId, string DisplayName, LocalDate Today, IReadOnlyList<StaffTimeOff> Upcoming, IReadOnlyList<StaffTimeOff> Past, int PastTotal)
{
    public const int PastShown = 10;
}

/// <summary>Reads through the tenant filter only: another salon's id is simply not found. "Today" is the salon's local date (rule 5).</summary>
public sealed class GetTimeOffHandler(SalonDbContext db, ITenantContext tenantContext, ITenantLookup tenantLookup, IClock clock) : IRequestHandler<GetTimeOffQuery, StaffTimeOffPage?>
{
    public async Task<StaffTimeOffPage?> HandleAsync(GetTimeOffQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var staff = await db.StaffMembers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);
        if (staff is null)
        {
            return null;
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(GetTimeOffQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken);
        var zone = (tenant is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId)) ?? DateTimeZone.Utc;
        var today = clock.GetCurrentInstant().InZone(zone).Date;

        var upcoming = await db.StaffTimeOff.AsNoTracking()
            .Where(t => t.StaffMemberId == staff.Id && t.ToDate >= today)
            .OrderBy(t => t.FromDate).ThenBy(t => t.Start).ThenBy(t => t.Id)
            .ToListAsync(cancellationToken);
        var pastQuery = db.StaffTimeOff.AsNoTracking().Where(t => t.StaffMemberId == staff.Id && t.ToDate < today);
        var pastTotal = await pastQuery.CountAsync(cancellationToken);
        var past = await pastQuery
            .OrderByDescending(t => t.FromDate).ThenByDescending(t => t.Start).ThenByDescending(t => t.Id)
            .Take(StaffTimeOffPage.PastShown)
            .ToListAsync(cancellationToken);

        return new StaffTimeOffPage(staff.Id, staff.DisplayName, today, upcoming, past, pastTotal);
    }
}