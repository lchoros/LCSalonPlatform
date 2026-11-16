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
        var takings = Takings(lines, zone, In);
        takings = takings with { Daily = Daily(takings.ByDay, period, In) };

        return new SalonReport(
            period,
            resolved.Preset,
            resolved.Refused,
            today,
            takings,
            Visits(visits, completed, In),
            new FeesReport(
                In(lines.Where(l => ClientAccountRules.IsFee(l.Kind)).Sum(l => l.Amount)),
                In(lines.Where(l => l.Kind == ClientAccountEntryKind.FeeWaived).Sum(l => l.Amount)),
                In(lines.Where(l => l.Kind == ClientAccountEntryKind.FeePayment).Sum(l => l.Amount))),
            await TopServicesAsync(completed, In, cancellationToken).ConfigureAwait(false),
            await StaffAsync(visits, period, In, cancellationToken).ConfigureAwait(false),
            await RetailAsync(lines, takings.Total, start, end, In, cancellationToken).ConfigureAwait(false),
            await ComparisonAsync(period, today, zone, lines, visits, In, cancellationToken).ConfigureAwait(false),
            unpaid,
            unpaidCount,
            In(unpaidTotal));
    }

    public async Task<MoneyJournal> GetMoneyLinesAsync(ReportPreset? preset, LocalDate? fromDay, LocalDate? toDay, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Reports belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var period = ReportRules.Resolve(preset, fromDay, toDay, today).Period;
        var start = zone.AtStartOfDay(period.From).ToInstant();
        var end = zone.AtStartOfDay(period.To.PlusDays(1)).ToInstant();

        // Only lines with a method moved money, exactly as on the till and in the takings (ADR 0024).
        var rows = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.Method != null && e.At >= start && e.At < end)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.Kind, Method = e.Method!.Value, e.Amount, e.Currency, e.At, e.ClientId, e.AppointmentId, e.ByUserId, e.ProviderReference })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        rows = rows.Where(r => TillRules.Direction(r.Kind) != 0).ToList();

        var clientIds = rows.Where(r => r.ClientId is not null).Select(r => r.ClientId!.Value).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken).ConfigureAwait(false);
        var appointmentIds = rows.Where(r => r.AppointmentId is not null).Select(r => r.AppointmentId!.Value).Distinct().ToArray();
        var services = await (
            from a in db.Appointments.AsNoTracking()
            join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
            where appointmentIds.Contains(a.Id)
            select new { a.Id, s.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken).ConfigureAwait(false);
        var userIds = rows.Where(r => r.ByUserId is not null).Select(r => r.ByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken).ConfigureAwait(false);

        var lines = rows
            .Select(r => new MoneyLine(
                r.At.InZone(zone),
                r.Kind,
                r.Method,
                new Money(TillRules.Direction(r.Kind) * r.Amount, r.Currency),
                r.ClientId is { } clientId ? clients.GetValueOrDefault(clientId) ?? string.Empty : string.Empty,
                r.AppointmentId is { } visitId ? services.GetValueOrDefault(visitId) : null,
                r.ByUserId is { } userId ? users.GetValueOrDefault(userId) : null,
                r.ProviderReference))
            .ToArray();
        return new MoneyJournal(period, lines);
    }

    public async Task<VisitJournal> GetVisitLinesAsync(ReportPreset? preset, LocalDate? fromDay, LocalDate? toDay, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Reports belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var period = ReportRules.Resolve(preset, fromDay, toDay, today).Period;
        var start = zone.AtStartOfDay(period.From).ToInstant();
        var end = zone.AtStartOfDay(period.To.PlusDays(1)).ToInstant();

        // A visit counts on the day it starts, exactly as in the report (ADR 0026).
        var rows = await (
            from a in db.Appointments.AsNoTracking()
            join c in db.Clients.AsNoTracking() on a.ClientId equals c.Id
            join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
            join m in db.StaffMembers.AsNoTracking() on a.StaffMemberId equals m.Id
            where a.Start >= start && a.Start < end
            orderby a.Start, a.Id
            select new { a.Start, c.FirstName, c.LastName, Service = s.Name, Staff = m.DisplayName, a.DurationMinutes, a.Status, a.Source, a.PriceAmount, a.Currency })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var lines = rows
            .Select(r => new VisitLine(
                r.Start.InZone(zone),
                r.FirstName + " " + r.LastName,
                r.Service,
                r.Staff,
                r.DurationMinutes,
                r.Status,
                r.Source,
                new Money(r.PriceAmount, r.Currency)))
            .ToArray();
        return new VisitJournal(period, lines);
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

    /// <summary>The takings of every day of a charted period (ADR 0048); nothing to draw while no day has money.</summary>
    private static DayTakings[] Daily(IReadOnlyList<DayTakings> byDay, ReportPeriod period, Func<decimal, Money> money)
    {
        if (byDay.Count == 0)
        {
            return [];
        }

        var amounts = byDay.ToDictionary(d => d.Day, d => d.Amount);
        return [.. ReportRules.ChartDays(period).Select(day => new DayTakings(day, amounts.TryGetValue(day, out var amount) ? amount : money(0m)))];
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

    /// <summary>
    /// Products over the counter (ADR 0044). The money is the period's product lines of the ledger, so it is the same
    /// money the takings show. The units come from the bill lines those sales paid for: a paid bill line points at its
    /// sale, and the sale's amount is the sum of its lines, so product by product adds up to what was sold.
    /// </summary>
    private async Task<RetailReport> RetailAsync(
        List<LedgerRow> lines,
        Money takings,
        Instant start,
        Instant end,
        Func<decimal, Money> money,
        CancellationToken cancellationToken)
    {
        var sold = lines.Where(l => l.Kind == ClientAccountEntryKind.ProductSale).Sum(l => l.Amount);
        var returned = lines.Where(l => l.Kind == ClientAccountEntryKind.ProductReturn).Sum(l => l.Amount);
        if (sold == 0m)
        {
            // Nothing was sold; a return of something sold earlier is still money that went out. No share of the takings
            // is given: what went out is not a part of what came in.
            return new RetailReport(money(0m), money(returned), money(-returned), 0, 0, null, [], 0);
        }

        var onBills = await (
            from line in db.VisitProductLines.AsNoTracking()
            join sale in db.ClientAccountEntries.AsNoTracking() on line.PaymentEntryId equals sale.Id
            where sale.At >= start && sale.At < end
            select new SoldRow(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.UnitCost, line.ReturnedQuantity, line.AppointmentId, null, line.AddedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Sold at the counter without a visit (ADR 0053): the same money kind, so the same units and the same profit.
        var atCounter = await (
            from line in db.CounterSaleLines.AsNoTracking()
            join sale in db.ClientAccountEntries.AsNoTracking() on line.PaymentEntryId equals sale.Id
            where sale.At >= start && sale.At < end
            select new SoldRow(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.UnitCost, line.ReturnedQuantity, null, line.PaymentEntryId, sale.At))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var rows = onBills.Concat(atCounter).ToList();
        var ids = rows.Select(r => r.ProductId).Distinct().ToArray();
        var names = await db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken).ConfigureAwait(false);

        var products = rows
            .GroupBy(r => r.ProductId)
            .Select(g => new ProductSalesLine(
                names.TryGetValue(g.Key, out var name) ? name : g.OrderByDescending(r => r.AddedAt).First().ProductName,
                g.Sum(r => r.Quantity),
                money(g.Sum(r => ProductSaleRules.LineTotal(r.Quantity, r.UnitPrice))))
            {
                Profit = ProductSaleRules.Profit(g.Select(r => (r.Quantity - r.ReturnedQuantity, r.UnitPrice, r.UnitCost))).Amount is { } kept ? money(kept) : null,
            })
            .OrderByDescending(p => p.Value.Amount).ThenByDescending(p => p.Units).ThenBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();

        var profit = ProductSaleRules.Profit(rows.Select(r => (r.Quantity - r.ReturnedQuantity, r.UnitPrice, r.UnitCost)));
        return new RetailReport(
            money(sold),
            money(returned),
            money(sold - returned),
            rows.Sum(r => r.Quantity),
            onBills.Select(r => r.AppointmentId).Distinct().Count(),
            ReportRules.Share(sold - returned, takings.Amount),
            products.Take(SalonReport.MaxTopProducts).ToArray(),
            products.Length)
        {
            CounterSales = atCounter.Select(r => r.CounterSaleId).Distinct().Count(),
            Profit = profit.Amount is { } amount ? money(amount) : null,
            ProfitShare = profit.Percent,
            UnitsWithoutCost = profit.UnitsWithoutCost,
        };
    }

    /// <summary>
    /// The period against the same days one period earlier (ADR 0046). The days so far come from the rows the report
    /// already holds; the days before cost two small queries. Both sides are added up with the rules the report
    /// itself uses, so a figure here is the figure that period's own report shows.
    /// </summary>
    private async Task<ReportComparison?> ComparisonAsync(
        ReportPeriod period,
        LocalDate today,
        DateTimeZone zone,
        List<LedgerRow> lines,
        List<VisitRow> visits,
        Func<decimal, Money> money,
        CancellationToken cancellationToken)
    {
        if (ReportRules.Compare(period, today) is not { } compared)
        {
            return null;
        }

        var soFarEnd = zone.AtStartOfDay(compared.SoFar.To.PlusDays(1)).ToInstant();
        var beforeStart = zone.AtStartOfDay(compared.Before.From).ToInstant();
        var beforeEnd = zone.AtStartOfDay(compared.Before.To.PlusDays(1)).ToInstant();

        var linesBefore = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.At >= beforeStart && e.At < beforeEnd)
            .Select(e => new LedgerRow(e.Kind, e.Method, e.Amount, e.At))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var statusesBefore = await db.Appointments.AsNoTracking()
            .Where(a => a.Start >= beforeStart && a.Start < beforeEnd)
            .Select(a => a.Status)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var takingsNow = Takings(lines.Where(l => l.At < soFarEnd).ToList(), zone, money).Total;
        var takingsBefore = Takings(linesBefore, zone, money).Total;
        var statusesNow = visits.Where(v => v.Start < soFarEnd).Select(v => v.Status).ToArray();
        var completedNow = statusesNow.Count(s => s == AppointmentStatus.Completed);
        var completedBefore = statusesBefore.Count(s => s == AppointmentStatus.Completed);
        var noShowsNow = statusesNow.Count(s => s == AppointmentStatus.NoShow);
        var noShowsBefore = statusesBefore.Count(s => s == AppointmentStatus.NoShow);

        return new ReportComparison(
            compared.SoFar,
            compared.Before,
            takingsNow,
            takingsBefore,
            ReportRules.Change(takingsNow.Amount, takingsBefore.Amount),
            completedNow,
            completedBefore,
            ReportRules.Change(completedNow, completedBefore),
            noShowsNow,
            noShowsBefore,
            ReportRules.Change(noShowsNow, noShowsBefore));
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
                && (e.Kind == ClientAccountEntryKind.VisitPayment || e.Kind == ClientAccountEntryKind.DepositPaid || e.Kind == ClientAccountEntryKind.DepositReturned
                    || e.Kind == ClientAccountEntryKind.VoucherRedeemed)
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

            // A deposit is returned in full (ADR 0023), so what is held is what came in less what went back. A gift
            // voucher used toward the visit counts the same way (ADR 0057).
            var held = own.Sum(m => m.Kind == ClientAccountEntryKind.DepositReturned ? -m.Amount : m.Amount);
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

    /// <summary>One product line that was paid for in the period: on a visit's bill, or at the counter (ADR 0053).</summary>
    private sealed record SoldRow(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal? UnitCost, int ReturnedQuantity, Guid? AppointmentId, Guid? CounterSaleId, Instant AddedAt);

    private sealed record LedgerRow(ClientAccountEntryKind Kind, PaymentMethod? Method, decimal Amount, Instant At);

    private sealed record VisitRow(Guid Id, AppointmentStatus Status, AppointmentSource Source, Guid StaffMemberId, Guid ServiceId, Guid ClientId, decimal Price, int Minutes, Instant Start);
}