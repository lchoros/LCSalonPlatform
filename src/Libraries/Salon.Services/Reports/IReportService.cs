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

    /// <summary>
    /// Every line of the period that moved money (ADR 0042), oldest first, for a spreadsheet: when, who, what for, how
    /// and how much, money out as a negative amount. The same lines the takings above are added up from, so the file
    /// and the report never disagree. The period is resolved like <see cref="GetAsync"/> does.
    /// </summary>
    Task<MoneyJournal> GetMoneyLinesAsync(ReportPreset? preset, LocalDate? fromDay, LocalDate? toDay, CancellationToken cancellationToken = default);
}

/// <param name="Preset">The one-tap period this is, if any.</param>
/// <param name="RangeRefused">Dates were asked for that cannot be used; the report shows the default period instead.</param>
/// <param name="Today">The salon's current local day.</param>
/// <param name="Retail">Products sold over the counter (ADR 0044).</param>
/// <param name="Comparison">The period against the one before (ADR 0046); null for a period that has not begun.</param>
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
    RetailReport Retail,
    ReportComparison? Comparison,
    IReadOnlyList<UnpaidVisit> Unpaid,
    int UnpaidCount,
    Money UnpaidTotal)
{
    public const int MaxTopServices = 10;
    public const int MaxTopProducts = 10;
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


/// <summary>
/// What the salon sold over the counter in the period (ADR 0044). Money counts on the day it was recorded, like the
/// takings, and the units are those of the bills paid in the period, so the products add up to the money.
/// </summary>
/// <param name="Sold">The product lines of the bills paid in the period.</param>
/// <param name="Returned">Money given back in the period for products brought back, whenever they were sold.</param>
/// <param name="Net"><paramref name="Sold"/> less <paramref name="Returned"/>.</param>
/// <param name="Units">Units on the bills paid in the period.</param>
/// <param name="Bills">Visits whose bill, paid in the period, had a product on it.</param>
/// <param name="ShareOfTakings"><paramref name="Net"/> as a percentage of everything taken; null while nothing was taken.</param>
/// <param name="TopProducts">By value, at most <see cref="SalonReport.MaxTopProducts"/>.</param>
/// <param name="ProductCount">How many different products were sold in all.</param>
public sealed record RetailReport(
    Money Sold,
    Money Returned,
    Money Net,
    int Units,
    int Bills,
    decimal? ShareOfTakings,
    IReadOnlyList<ProductSalesLine> TopProducts,
    int ProductCount)
{
    /// <summary>Whether the period has anything to say about products: a sale or a return.</summary>
    public bool HasAny => Sold.Amount > 0m || Returned.Amount > 0m;

    public bool HasReturns => Returned.Amount > 0m;

    /// <summary><see cref="Returned"/> as money that went out: below zero, as the takings show it.</summary>
    public Money ReturnedOut => Returned with { Amount = -Returned.Amount };

    /// <summary>More products were sold than <see cref="TopProducts"/> names.</summary>
    public bool HasMoreProducts => ProductCount > TopProducts.Count;
}

/// <param name="Name">The product's name today; the name on the bill for one that is gone.</param>
/// <param name="Value">What the units were rung up at.</param>
public sealed record ProductSalesLine(string Name, int Units, Money Value);

/// <summary>
/// The period set against the same days one period earlier (ADR 0046). Both sides count only days that have
/// happened, so a week or month that is not over is compared like for like.
/// </summary>
/// <param name="SoFar">The days of the period up to today.</param>
/// <param name="Before">The days they are set against.</param>
/// <param name="TakingsChange">Percent more or less than before; null when nothing was taken before.</param>
public sealed record ReportComparison(
    ReportPeriod SoFar,
    ReportPeriod Before,
    Money Takings,
    Money TakingsBefore,
    decimal? TakingsChange,
    int Completed,
    int CompletedBefore,
    decimal? CompletedChange,
    int NoShows,
    int NoShowsBefore,
    decimal? NoShowsChange)
{
    /// <summary>Whether either side has anything in it; two empty periods are not worth a table.</summary>
    public bool HasAny => Takings.Amount != 0m || TakingsBefore.Amount != 0m || Completed + CompletedBefore + NoShows + NoShowsBefore > 0;
}
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

/// <summary>The money lines of a period (ADR 0042).</summary>
public sealed record MoneyJournal(ReportPeriod Period, IReadOnlyList<MoneyLine> Lines);

/// <summary>One line that moved money, as an accountant reads it.</summary>
/// <param name="At">In the salon's zone.</param>
/// <param name="Amount">Signed: money in is above zero, money out below.</param>
/// <param name="ServiceName">The service of the visit the line is about; null when it has none.</param>
/// <param name="RecordedBy">Display name of the login that recorded it; null when it is gone or unknown.</param>
public sealed record MoneyLine(
    ZonedDateTime At,
    ClientAccountEntryKind Kind,
    PaymentMethod Method,
    Money Amount,
    string ClientName,
    string? ServiceName,
    string? RecordedBy,
    string? Reference);