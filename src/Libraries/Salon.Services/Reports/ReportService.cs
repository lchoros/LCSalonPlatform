using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Payments;
using Salon.Services.Tenancy;

namespace Salon.Services.Reports;

/// <summary>
/// Reads the period's ledger lines and visits through the tenant filter and adds them up in memory with
/// <see cref="TillRules"/> and <see cref="ReportRules"/> (ADR 0026). A salon's period is a few thousand rows at most, and
/// adding up with the till's own rules is what keeps a report and the till from ever disagreeing.
/// </summary>
internal sealed class ReportService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock) : IReportService
{
    public async Task<SalonReport> GetAsync(ReportPreset? preset, LocalDate? fromDay, LocalDate? toDay, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Reports belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var resolved = ReportRules.Resolve(preset, fromDay, toDay, today);
        var period = resolved.Period;
        var start = zone.AtStartOfDay(period.From).ToInstant();
        var end = zone.AtStartOfDay(period.To.PlusDays(1)).ToInstant();
        Money In(decimal amount) => new(amount, tenant.Currency);

        var lines = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.At >= start && e.At < end)
            .Select(e => new LedgerRow(e.Kind, e.Method, e.Amount, e.At))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var visits = await db.Appointments.AsNoTracking()
            .Where(a => a.Start >= start && a.Start < end)
            .OrderBy(a => a.Start).ThenBy(a => a.Id)
            .Select(a => new VisitRow(a.Id, a.Status, a.Source, a.StaffMemberId, a.ServiceId, a.ClientId, a.PriceAmount, a.DurationMinutes, a.Start))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var completed = visits.Where(v => v.Status == AppointmentStatus.Completed).ToArray();

        var (unpaid, unpaidCount, unpaidTotal) = await UnpaidAsync(completed, start, end, zone, In, cancellationToken).ConfigureAwait(false);

        return new SalonReport(
            period,
            resolved.Preset,
            resolved.Refused,
            today,
            Takings(lines, zone, In),
            Visits(visits, completed, In),
            new FeesReport(
                In(lines.Where(l => ClientAccountRules.IsFee(l.Kind)).Sum(l => l.Amount)),
                In(lines.Where(l => l.Kind == ClientAccountEntryKind.FeeWaived).Sum(l => l.Amount)),
                In(lines.Where(l => l.Kind == ClientAccountEntryKind.FeePayment).Sum(l => l.Amount))),
            await TopServicesAsync(completed, In, cancellationToken).ConfigureAwait(false),
            await StaffAsync(visits, period, In, cancellationToken).ConfigureAwait(false),
            unpaid,
            unpaidCount,
            In(unpaidTotal));
    }

    private static TakingsReport Takings(List<LedgerRow> lines, DateTimeZone zone, Func<decimal, Money> money)
    {
        // Only lines with a method moved money, exactly as on the till (ADR 0024).
        var moved = lines.Where(l => l.Method is not null && TillRules.Direction(l.Kind) != 0).ToArray();
        var byMethod = TillRules.Totals(moved.Select(l => (l.Kind, l.Method!.Value, l.Amount)))
            .Select(t => new MethodTotal(t.Method, money(t.Amount)))
            .ToArray();
        var byKind = moved
            .GroupBy(l => l.Kind)
            .OrderBy(g => g.Key)
            .Select(g => new KindTotal(g.Key, money(TillRules.Direction(g.Key) * g.Sum(l => l.Amount))))
            .ToArray();
        var byDay = moved
            .GroupBy(l => l.At.InZone(zone).Date)
            .OrderBy(g => g.Key)
            .Select(g => new DayTakings(g.Key, money(g.Sum(l => TillRules.Direction(l.Kind) * l.Amount))))
            .ToArray();
        return new TakingsReport(money(byMethod.Sum(t => t.Amount.Amount)), byMethod, byKind, byDay);
    }

    private static VisitsReport Visits(List<VisitRow> visits, VisitRow[] completed, Func<decimal, Money> money)
    {
        var noShows = visits.Count(v => v.Status == AppointmentStatus.NoShow);
        return new VisitsReport(
            visits.Count,
            completed.Length,
            visits.Count(v => AppointmentStateMachine.Active.Contains(v.Status)),
            noShows,
            visits.Count(v => v.Status == AppointmentStatus.CancelledByClient),
            visits.Count(v => v.Status == AppointmentStatus.CancelledBySalon),
            visits.Count(v => v.Source == AppointmentSource.Online),
            ReportRules.NoShowRate(completed.Length, noShows),
            money(completed.Sum(v => v.Price)));
    }

    private async Task<IReadOnlyList<ServiceLine>> TopServicesAsync(VisitRow[] completed, Func<decimal, Money> money, CancellationToken cancellationToken)
    {
        var top = completed
            .GroupBy(v => v.ServiceId)
            .Select(g => (ServiceId: g.Key, Visits: g.Count(), Value: g.Sum(v => v.Price)))
            .OrderByDescending(s => s.Visits).ThenByDescending(s => s.Value).ThenBy(s => s.ServiceId)
            .Take(SalonReport.MaxTopServices)
            .ToArray();
        var ids = top.Select(s => s.ServiceId).ToArray();
        var names = await db.Services.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken).ConfigureAwait(false);
        return top.Select(s => new ServiceLine(names.TryGetValue(s.ServiceId, out var name) ? name : string.Empty, s.Visits, money(s.Value))).ToArray();
    }

    private async Task<IReadOnlyList<StaffLine>> StaffAsync(List<VisitRow> visits, ReportPeriod period, Func<decimal, Money> money, CancellationToken cancellationToken)
    {
        var people = await db.StaffMembers.AsNoTracking()
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new { s.Id, s.DisplayName, Works = s.IsActive && s.IsBookable })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var hours = await db.WorkingHours.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var timeOff = await db.StaffTimeOff.AsNoTracking()
            .Where(t => t.FromDate <= period.To && t.ToDate >= period.From)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var result = new List<StaffLine>();
        foreach (var person in people)
        {
            var own = visits.Where(v => v.StaffMemberId == person.Id).ToArray();
            var done = own.Where(v => v.Status == AppointmentStatus.Completed).ToArray();
            var booked = own.Where(v => v.Status == AppointmentStatus.Completed || AppointmentStateMachine.Active.Contains(v.Status)).Sum(v => v.Minutes);
            var working = person.Works
                ? WorkingMinutes(period, hours.Where(h => h.StaffMemberId == person.Id).ToArray(), timeOff.Where(t => t.StaffMemberId == person.Id).ToArray())
                : 0;
            if (working == 0 && booked == 0 && done.Length == 0)
            {
                continue;
            }

            result.Add(new StaffLine(person.DisplayName, done.Length, money(done.Sum(v => v.Price)), booked, working, ReportRules.Utilisation(booked, working)));
        }

        return result;
    }

    /// <summary>The rota applied to every day of the period, less the absences that touch it.</summary>
    private static int WorkingMinutes(ReportPeriod period, WorkingHours[] hours, StaffTimeOff[] timeOff)
    {
        if (hours.Length == 0)
        {
            return 0;
        }

        var rota = hours.ToLookup(h => h.Day, h => h.Range);
        var minutes = 0;
        for (var day = period.From; day <= period.To; day = day.PlusDays(1))
        {
            var away = timeOff.Where(t => t.FromDate <= day && t.ToDate >= day).ToArray();
            if (away.Any(t => t.IsWholeDay))
            {
                continue;
            }

            minutes += ReportRules.WorkingMinutes(rota[day.DayOfWeek], away.Where(t => t.Range is not null).Select(t => t.Range!.Value));
        }

        return minutes;
    }

    /// <summary>
    /// Completed visits nobody took payment for: no payment line and a price the deposit held does not cover. The first
    /// <see cref="SalonReport.MaxUnpaidListed"/> are named; the count and the sum cover them all.
    /// </summary>
    private async Task<(IReadOnlyList<UnpaidVisit> Listed, int Count, decimal Total)> UnpaidAsync(
        VisitRow[] completed,
        Instant start,
        Instant end,
        DateTimeZone zone,
        Func<decimal, Money> money,
        CancellationToken cancellationToken)
    {
        if (completed.Length == 0)
        {
            return ([], 0, 0m);
        }

        var paid = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.AppointmentId != null
                && (e.Kind == ClientAccountEntryKind.VisitPayment || e.Kind == ClientAccountEntryKind.DepositPaid || e.Kind == ClientAccountEntryKind.DepositReturned)
                && db.Appointments.Any(a => a.Id == e.AppointmentId && a.Start >= start && a.Start < end && a.Status == AppointmentStatus.Completed))
            .Select(e => new { AppointmentId = e.AppointmentId!.Value, e.Kind, e.Amount })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byVisit = paid.ToLookup(p => p.AppointmentId);

        var open = new List<(VisitRow Visit, decimal Outstanding)>();
        foreach (var visit in completed)
        {
            var own = byVisit[visit.Id].ToArray();
            if (own.Any(m => m.Kind == ClientAccountEntryKind.VisitPayment))
            {
                continue;
            }

            // A deposit is returned in full (ADR 0023), so what is held is what came in less what went back.
            var held = own.Sum(m => m.Kind == ClientAccountEntryKind.DepositPaid ? m.Amount : -m.Amount);
            var outstanding = TillRules.Outstanding(visit.Price, held);
            if (outstanding > 0m)
            {
                open.Add((visit, outstanding));
            }
        }

        var listed = open.Take(SalonReport.MaxUnpaidListed).ToArray();
        var clientIds = listed.Select(o => o.Visit.ClientId).Distinct().ToArray();
        var serviceIds = listed.Select(o => o.Visit.ServiceId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken).ConfigureAwait(false);
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken).ConfigureAwait(false);

        return (
            listed.Select(o => new UnpaidVisit(
                o.Visit.Id,
                o.Visit.Start.InZone(zone),
                clients.TryGetValue(o.Visit.ClientId, out var client) ? client : string.Empty,
                services.TryGetValue(o.Visit.ServiceId, out var service) ? service : string.Empty,
                money(o.Outstanding))).ToArray(),
            open.Count,
            open.Sum(o => o.Outstanding));
    }

    private sealed record LedgerRow(ClientAccountEntryKind Kind, PaymentMethod? Method, decimal Amount, Instant At);

    private sealed record VisitRow(Guid Id, AppointmentStatus Status, AppointmentSource Source, Guid StaffMemberId, Guid ServiceId, Guid ClientId, decimal Price, int Minutes, Instant Start);
}