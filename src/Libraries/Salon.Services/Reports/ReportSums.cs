using Microsoft.EntityFrameworkCore;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Core.Staff;
using Salon.Data;

namespace Salon.Services.Reports;

/// <summary>
/// The sums the period report (ADR 0026) and today's glance (ADR 0084) must agree on, in one place: how much of a
/// span a person is at work, and which completed visits nobody took payment for. Both read through the tenant filter.
/// </summary>
internal static class ReportSums
{
    /// <summary>
    /// Whether a visit that took place or still stands fills a person's time: completed, or booked, confirmed or
    /// arrived. A no-show and a cancellation do not.
    /// </summary>
    public static bool FillsTime(AppointmentStatus status)
        => status == AppointmentStatus.Completed || AppointmentStateMachine.Active.Contains(status);

    /// <summary>The rota applied to every day of the period, less the absences that touch it.</summary>
    public static int WorkingMinutes(ReportPeriod period, IReadOnlyCollection<WorkingHours> hours, IReadOnlyCollection<StaffTimeOff> timeOff)
    {
        if (hours.Count == 0)
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
    /// Of the completed visits that start between <paramref name="start"/> and <paramref name="end"/>, those nobody
    /// took payment for, in the order given, each with what is left to pay: no payment line and a price that what the
    /// salon holds toward the visit does not cover.
    /// </summary>
    public static async Task<IReadOnlyList<(Guid VisitId, decimal Outstanding)>> UnpaidAsync(
        SalonDbContext db,
        IReadOnlyList<(Guid Id, decimal Price)> completed,
        Instant start,
        Instant end,
        CancellationToken cancellationToken)
    {
        if (completed.Count == 0)
        {
            return [];
        }

        var paid = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.AppointmentId != null
                && (e.Kind == ClientAccountEntryKind.VisitPayment || e.Kind == ClientAccountEntryKind.DepositPaid || e.Kind == ClientAccountEntryKind.DepositReturned
                    || e.Kind == ClientAccountEntryKind.VoucherRedeemed || e.Kind == ClientAccountEntryKind.PartPayment)
                && db.Appointments.Any(a => a.Id == e.AppointmentId && a.Start >= start && a.Start < end && a.Status == AppointmentStatus.Completed))
            .Select(e => new { AppointmentId = e.AppointmentId!.Value, e.Kind, e.Amount })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byVisit = paid.ToLookup(p => p.AppointmentId);

        var open = new List<(Guid VisitId, decimal Outstanding)>();
        foreach (var (id, price) in completed)
        {
            var own = byVisit[id].ToArray();
            if (own.Any(m => m.Kind == ClientAccountEntryKind.VisitPayment))
            {
                continue;
            }

            // A deposit is returned in full (ADR 0023), so what is held is what came in less what went back. A gift
            // voucher used toward the visit counts the same way (ADR 0057), and so does a part paid before the rest
            // (ADR 0061): a completed visit with a part paid owes what is left, not its whole price.
            var held = own.Sum(m => m.Kind == ClientAccountEntryKind.DepositReturned ? -m.Amount : m.Amount);
            var outstanding = TillRules.Outstanding(price, held);
            if (outstanding > 0m)
            {
                open.Add((id, outstanding));
            }
        }

        return open;
    }
}