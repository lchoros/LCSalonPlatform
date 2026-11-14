using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Reports;
using Salon.Services.Payments;

namespace Salon.Services.Reports;

/// <summary>
/// The salon's numbers for a span of days (ADR 0026): money from the client account ledger, visits from the diary.
/// Read only. Money counts on the day it was recorded, a visit on the day it starts, both in the salon's zone, so the
/// takings of a day here are the till of that day.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// The report for two dates, a preset, or this month when neither is usable (<see cref="ReportRules.Resolve"/>).
    /// </summary>
    Task<SalonReport> GetAsync(ReportPreset? preset, LocalDate? fromDay, LocalDate? toDay, CancellationToken cancellationToken = default);
}

/// <param name="Preset">The one-tap period this is, if any.</param>
/// <param name="RangeRefused">Dates were asked for that cannot be used; the report shows the default period instead.</param>
/// <param name="Today">The salon's current local day.</param>
/// <param name="Unpaid">Completed visits of the period nobody took payment for, oldest first, at most <see cref="MaxUnpaidListed"/>.</param>
/// <param name="UnpaidCount">How many there are in all.</param>
public sealed record SalonReport(
    ReportPeriod Period,
    ReportPreset? Preset,
    bool RangeRefused,
    LocalDate Today,
    TakingsReport Takings,
    VisitsReport Visits,
    FeesReport Fees,
    IReadOnlyList<ServiceLine> TopServices,
    IReadOnlyList<StaffLine> Staff,
    IReadOnlyList<UnpaidVisit> Unpaid,
    int UnpaidCount,
    Money UnpaidTotal)
{
    public const int MaxTopServices = 10;
    public const int MaxUnpaidListed = 50;
}

/// <param name="Total">Everything that came in less everything that went out, all methods.</param>
/// <param name="ByMethod">Net per method, methods nobody used left out.</param>
/// <param name="ByKind">Net per kind of line; a deposit returned is negative.</param>
/// <param name="ByDay">Net per day, days without money left out.</param>
public sealed record TakingsReport(Money Total, IReadOnlyList<MethodTotal> ByMethod, IReadOnlyList<KindTotal> ByKind, IReadOnlyList<DayTakings> ByDay);

public sealed record KindTotal(ClientAccountEntryKind Kind, Money Amount);

public sealed record DayTakings(LocalDate Day, Money Amount);

/// <param name="Total">Every visit that starts in the period, whatever became of it.</param>
/// <param name="Open">Booked, confirmed or arrived: no outcome yet.</param>
/// <param name="Online">Of <paramref name="Total"/>, booked by the client on the public page.</param>
/// <param name="NoShowRate">Percent; null while no visit of the period has an outcome.</param>
/// <param name="CompletedValue">The booked prices of the completed visits: what the work was worth, paid or not.</param>
public sealed record VisitsReport(
    int Total,
    int Completed,
    int Open,
    int NoShows,
    int CancelledByClient,
    int CancelledBySalon,
    int Online,
    decimal? NoShowRate,
    Money CompletedValue);

/// <summary>Fees by what happened to them in the period: added to an account, let go, or paid.</summary>
public sealed record FeesReport(Money Added, Money Waived, Money Paid);

/// <param name="Visits">Completed visits.</param>
public sealed record ServiceLine(string Name, int Visits, Money Value);

/// <param name="Visits">Completed visits.</param>
/// <param name="BookedMinutes">Visits that took place or still stand; no-shows and cancellations are left out.</param>
/// <param name="WorkingMinutes">From today's rota less time off, so an old period is an estimate if the rota changed.</param>
/// <param name="Utilisation">Percent; null for somebody without working time in the period.</param>
public sealed record StaffLine(string Name, int Visits, Money Value, int BookedMinutes, int WorkingMinutes, decimal? Utilisation);

/// <param name="Start">In the salon's zone.</param>
/// <param name="Outstanding">The booked price less the deposit the salon holds.</param>
public sealed record UnpaidVisit(Guid AppointmentId, ZonedDateTime Start, string ClientName, string ServiceName, Money Outstanding);