using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Appointments;
using Salon.Services.Tenancy;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Reads through the tenant filter only: the staff and their rota (ADR 0010), the active appointments in the window
/// (ADR 0012) and the client first names and service names they show. Health notes are never read (ADR 0011). The pure
/// <see cref="CalendarLayout"/> turns the rota into rows and the appointments into positioned blocks.
/// </summary>
public sealed class CalendarHandler(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IRequestHandler<CalendarQuery, CalendarBoard>
{
    public async Task<CalendarBoard> HandleAsync(CalendarQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The tenant guard has already refused tenant-less requests; this only narrows the type.
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException($"{nameof(CalendarQuery)} requires a tenant in scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant {tenantId} is in scope but has no row.");
        var zone = DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
        var day = request.Date ?? clock.GetCurrentInstant().InZone(zone).Date;
        var (from, to) = request.View == CalendarView.Week
            ? (day.PlusDays(-((int)day.DayOfWeek - 1)), day.PlusDays(7 - (int)day.DayOfWeek))
            : (day, day);

        var windowStart = zone.AtStartOfDay(from).ToInstant();
        var windowEnd = zone.AtStartOfDay(to.PlusDays(1)).ToInstant();
        var active = AppointmentStateMachine.Active.ToArray();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => active.Contains(a.Status) && a.Start >= windowStart && a.Start < windowEnd)
            .OrderBy(a => a.Start)
            .Select(a => new { a.Id, a.StaffMemberId, a.ClientId, a.ServiceId, a.Start, a.End, a.Status })
            .ToListAsync(cancellationToken);

        var staffWithVisits = appointments.Select(a => a.StaffMemberId).Distinct().ToArray();
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => (s.IsActive && s.IsBookable) || staffWithVisits.Contains(s.Id))
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName)
            .Select(s => new CalendarStaff(s.Id, s.DisplayName, s.Colour))
            .ToListAsync(cancellationToken);
        var staffById = staff.ToDictionary(s => s.Id);

        Guid? selected = request.StaffMemberId is { } wanted && staffById.ContainsKey(wanted) ? wanted : null;
        if (request.View == CalendarView.Week && selected is null && staff.Count > 0)
        {
            selected = staff[0].Id;
        }

        var shown = selected is { } one ? [staffById[one]] : staff;
        var shownIds = shown.Select(s => s.Id).ToArray();
        var hours = await db.WorkingHours.AsNoTracking()
            .Where(w => shownIds.Contains(w.StaffMemberId))
            .ToListAsync(cancellationToken);

        var clientIds = appointments.Select(a => a.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FirstName, cancellationToken);
        var serviceIds = appointments.Select(a => a.ServiceId).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        // Week view: one column per day of the selected person; day view: one column per shown person.
        var cells = request.View == CalendarView.Week
            ? Enumerable.Range(0, 7).Select(i => (Date: from.PlusDays(i), Staff: shown[0])).ToList()
            : shown.Select(s => (Date: day, Staff: s)).ToList();

        var inputs = cells.Select(cell =>
        {
            var dayStart = zone.AtStartOfDay(cell.Date).ToInstant();
            var dayEnd = zone.AtStartOfDay(cell.Date.PlusDays(1)).ToInstant();
            var ranges = hours
                .Where(h => h.StaffMemberId == cell.Staff.Id && h.Day == cell.Date.DayOfWeek)
                .OrderBy(h => h.Start)
                .Select(h => h.Range)
                .ToArray();
            var blocks = appointments
                .Where(a => a.StaffMemberId == cell.Staff.Id && a.Start >= dayStart && a.Start < dayEnd)
                .Select(a => new BlockInput(a.Id, MinutesInto(dayStart, a.Start), MinutesInto(dayStart, a.End)))
                .ToArray();
            return (cell.Date, cell.Staff, Ranges: (IReadOnlyList<TimeRange>)ranges, Blocks: (IReadOnlyList<BlockInput>)blocks);
        }).ToList();

        var grid = CalendarLayout.Range(inputs.SelectMany(i => i.Ranges), inputs.SelectMany(i => i.Blocks));
        var byId = appointments.ToDictionary(a => a.Id);
        var columns = inputs
            .Select(i => new CalendarColumn(
                i.Date,
                i.Staff,
                i.Ranges,
                CalendarLayout.Position(grid, i.Blocks)
                    .Select(p =>
                    {
                        var a = byId[p.Id];
                        return new CalendarBlock(
                            a.Id,
                            a.Start.InZone(zone).TimeOfDay,
                            a.End.InZone(zone).TimeOfDay,
                            clients.GetValueOrDefault(a.ClientId, string.Empty),
                            services.GetValueOrDefault(a.ServiceId, string.Empty),
                            a.Status,
                            p.Row,
                            p.Span,
                            p.Lane,
                            p.Lanes);
                    })
                    .ToArray()))
            .ToArray();

        return new CalendarBoard(request.View, day, from, to, staff, selected, grid, columns);
    }

    /// <summary>Whole minutes from the start of the local day, kept inside the day (a visit that runs past midnight ends at 1440).</summary>
    private static int MinutesInto(Instant dayStart, Instant instant)
        => (int)Math.Clamp(Math.Round((instant - dayStart).TotalMinutes), 0, CalendarLayout.MinutesPerDay);
}