using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Tenancy;

namespace Salon.Services.Reports;

/// <summary>
/// Reads today's visits, the people who take bookings with their rota and time off, and the names the next visits
/// show, all through the tenant filter, and adds them up in memory: a salon's day is a few dozen rows. The money is
/// the day's ledger lines added up with the till's own rule (<see cref="TillRules.TotalsByMethod"/>), the occupancy
/// and the unpaid visits come from <see cref="ReportSums"/>, so this page has no rule of its own to drift
/// (ADR 0084). Of a client only the first name is ever selected; health notes and contact details are never read.
/// </summary>
internal sealed class TodayService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : ITodayService
{
    /// <summary>Stands in where names were not asked for or there is nobody to name; never written to.</summary>
    private static readonly Dictionary<Guid, string> NoNames = new();

    public async Task<TodayGlance> GetAsync(TodayRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Today at a glance belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var now = clock.GetCurrentInstant();
        var today = now.InZone(zone).Date;
        var start = zone.AtStartOfDay(today).ToInstant();
        var end = zone.AtStartOfDay(today.PlusDays(1)).ToInstant();

        // A visit counts on the day it starts, exactly as in the report (ADR 0026).
        var visits = await db.Appointments.AsNoTracking()
            .Where(a => a.Start >= start && a.Start < end)
            .OrderBy(a => a.Start).ThenBy(a => a.Id)
            .Select(a => new VisitRow(a.Id, a.Status, a.StaffMemberId, a.ServiceId, a.ClientId, a.PriceAmount, a.DurationMinutes, a.Start, a.End))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Still to come: it holds its time and has not ended. A client who is a few minutes late, or in the chair, is
        // still the desk's next concern; a visit whose time has passed is not, whatever its status says.
        var toCome = visits.Where(v => AppointmentStateMachine.IsActive(v.Status) && v.End > now).ToArray();
        var next = toCome.Take(TodayGlance.MaxNext).ToArray();

        var people = await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive && s.IsBookable)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.DisplayName, s.Colour })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var staff = await OccupancyAsync(people.Select(p => (p.Id, p.DisplayName, p.Colour)).ToArray(), visits, today, cancellationToken).ConfigureAwait(false);

        var money = request.Money
            ? await MoneyAsync(visits, start, end, zone, request.ClientNames, tenant.Currency, cancellationToken).ConfigureAwait(false)
            : null;

        return new TodayGlance(
            today,
            new TodayVisits(
                visits.Count,
                visits.Count(v => v.Status == AppointmentStatus.Booked),
                visits.Count(v => v.Status == AppointmentStatus.Confirmed),
                visits.Count(v => v.Status == AppointmentStatus.Arrived),
                visits.Count(v => v.Status == AppointmentStatus.Completed),
                visits.Count(v => v.Status == AppointmentStatus.NoShow),
                visits.Count(v => AppointmentStateMachine.IsCancelled(v.Status))),
            await NextAsync(next, zone, request.ClientNames, cancellationToken).ConfigureAwait(false),
            toCome.Length,
            staff,
            money);
    }

    private async Task<IReadOnlyList<UpcomingVisit>> NextAsync(VisitRow[] next, DateTimeZone zone, bool clientNames, CancellationToken cancellationToken)
    {
        if (next.Length == 0)
        {
            return [];
        }

        var clients = clientNames ? await FirstNamesAsync(next.Select(v => v.ClientId), cancellationToken).ConfigureAwait(false) : NoNames;
        var services = await ServiceNamesAsync(next.Select(v => v.ServiceId), cancellationToken).ConfigureAwait(false);

        // The person of a visit may have stopped taking bookings since; the visit still names them.
        var staffIds = next.Select(v => v.StaffMemberId).Distinct().ToArray();
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(s => staffIds.Contains(s.Id))
            .Select(s => new { s.Id, s.DisplayName, s.Colour })
            .ToDictionaryAsync(s => s.Id, cancellationToken).ConfigureAwait(false);

        return next
            .Select(v =>
            {
                var person = staff.GetValueOrDefault(v.StaffMemberId);
                return new UpcomingVisit(
                    v.Id,
                    v.Start.InZone(zone).TimeOfDay,
                    v.End.InZone(zone).TimeOfDay,
                    clients.GetValueOrDefault(v.ClientId, string.Empty),
                    services.GetValueOrDefault(v.ServiceId, string.Empty),
                    person?.DisplayName ?? string.Empty,
                    person?.Colour ?? Core.Staff.StaffMember.DefaultColour,
                    v.Status);
            })
            .ToArray();
    }

    /// <summary>Booked time over today's rota less time off, per person, with the sums the report uses.</summary>
    private async Task<IReadOnlyList<StaffOccupancy>> OccupancyAsync(
        (Guid Id, string Name, string Colour)[] people,
        List<VisitRow> visits,
        LocalDate today,
        CancellationToken cancellationToken)
    {
        if (people.Length == 0)
        {
            return [];
        }

        var ids = people.Select(p => p.Id).ToArray();
        var hours = await db.WorkingHours.AsNoTracking()
            .Where(h => ids.Contains(h.StaffMemberId) && h.Day == today.DayOfWeek)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var timeOff = await db.StaffTimeOff.AsNoTracking()
            .Where(t => ids.Contains(t.StaffMemberId) && t.FromDate <= today && t.ToDate >= today)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var day = new ReportPeriod(today, today);

        return people
            .Select(person =>
            {
                var booked = visits.Where(v => v.StaffMemberId == person.Id && ReportSums.FillsTime(v.Status)).Sum(v => v.Minutes);
                var working = ReportSums.WorkingMinutes(day, hours.Where(h => h.StaffMemberId == person.Id).ToArray(), timeOff.Where(t => t.StaffMemberId == person.Id).ToArray());
                return new StaffOccupancy(person.Id, person.Name, person.Colour, booked, working, ReportRules.Utilisation(booked, working));
            })
            .ToArray();
    }

    private async Task<TodayMoney> MoneyAsync(
        List<VisitRow> visits,
        Instant start,
        Instant end,
        DateTimeZone zone,
        bool clientNames,
        string currency,
        CancellationToken cancellationToken)
    {
        Money In(decimal amount) => new(amount, currency);

        // Only lines with a method moved money, exactly as on the till (ADR 0024), oldest first so one of the salon's
        // own methods reads under the name on its latest line (ADR 0060). Nobody is named: only the sums are needed.
        var lines = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.Method != null && e.At >= start && e.At < end)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.Kind, Method = e.Method!.Value, e.CustomMethodId, e.MethodName, e.Amount })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byMethod = TillRules.TotalsByMethod(lines.Select(l => new MethodLine(l.Kind, l.Method, l.CustomMethodId, l.MethodName, l.Amount)))
            .Select(t => new MethodTotal(t.Method, In(t.Amount)) { CustomMethodId = t.CustomMethodId, MethodName = t.MethodName })
            .ToArray();

        var completed = visits.Where(v => v.Status == AppointmentStatus.Completed).ToArray();
        var open = await ReportSums.UnpaidAsync(db, [.. completed.Select(v => (v.Id, v.Price))], start, end, cancellationToken).ConfigureAwait(false);

        var rows = completed.ToDictionary(v => v.Id);
        var listed = open.Take(TodayGlance.MaxUnpaidListed).Select(o => (Visit: rows[o.VisitId], o.Outstanding)).ToArray();
        var clients = clientNames && listed.Length > 0 ? await FirstNamesAsync(listed.Select(l => l.Visit.ClientId), cancellationToken).ConfigureAwait(false) : NoNames;
        var services = listed.Length > 0 ? await ServiceNamesAsync(listed.Select(l => l.Visit.ServiceId), cancellationToken).ConfigureAwait(false) : NoNames;

        return new TodayMoney(
            In(byMethod.Sum(t => t.Amount.Amount)),
            byMethod,
            listed
                .Select(l => new UnpaidToday(
                    l.Visit.Id,
                    l.Visit.Start.InZone(zone).TimeOfDay,
                    clients.GetValueOrDefault(l.Visit.ClientId, string.Empty),
                    services.GetValueOrDefault(l.Visit.ServiceId, string.Empty),
                    In(l.Outstanding)))
                .ToArray(),
            open.Count,
            In(open.Sum(o => o.Outstanding)));
    }

    /// <summary>First names only: this page never shows a surname or a way to reach a client.</summary>
    private Task<Dictionary<Guid, string>> FirstNamesAsync(IEnumerable<Guid> clientIds, CancellationToken cancellationToken)
    {
        var ids = clientIds.Distinct().ToArray();
        return db.Clients.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FirstName, cancellationToken);
    }

    private Task<Dictionary<Guid, string>> ServiceNamesAsync(IEnumerable<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var ids = serviceIds.Distinct().ToArray();
        return db.Services.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
    }

    private sealed record VisitRow(Guid Id, AppointmentStatus Status, Guid StaffMemberId, Guid ServiceId, Guid ClientId, decimal Price, int Minutes, Instant Start, Instant End);
}