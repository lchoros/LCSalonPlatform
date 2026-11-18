using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Payments;

namespace Salon.Services.Payments;

/// <summary>
/// The till of one salon day (ADR 0024): every line on the clients' accounts that carried a payment method, added up
/// per method, and the end-of-day count of the cash drawer. It reads the same ledger the checkout, the fees and the
/// deposits write, so the day always adds up to what was recorded; it never moves money itself.
/// </summary>
public interface ICashUpService
{
    /// <summary>The money of <paramref name="day"/> in the salon's zone (today when null), with the count if the day was closed.</summary>
    Task<CashDay> GetDayAsync(LocalDate? day, CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes <paramref name="day"/> with the cash the person counted. A day is closed once and not before it has come;
    /// the outcome says which, and nothing is written unless it is <see cref="CashUpOutcome.Closed"/>. Throws
    /// <see cref="ArgumentOutOfRangeException"/> for a count that is negative or finer than a penny.
    /// </summary>
    Task<CashUpResult> CloseAsync(LocalDate day, decimal countedCash, string? note, Guid? byUserId, CancellationToken cancellationToken = default);
}

/// <param name="Today">The salon's current local day, so a page can tell past, present and future.</param>
/// <param name="Movements">Money in and out during the day, oldest first; a return carries a negative amount.</param>
/// <param name="Totals">Net per method, methods nobody used left out.</param>
/// <param name="Takings">Everything that came in less everything that went out, all methods.</param>
/// <param name="ExpectedCash">What the drawer should hold from this day's lines, as they stand now.</param>
/// <param name="Closed">The count, once the day was closed.</param>
public sealed record CashDay(
    LocalDate Day,
    LocalDate Today,
    IReadOnlyList<CashMovement> Movements,
    IReadOnlyList<MethodTotal> Totals,
    Money Takings,
    Money ExpectedCash,
    CashUpRecord? Closed)
{
    /// <summary>True when money was recorded for the day after it was closed: the count no longer matches the lines.</summary>
    public bool ChangedSinceClose => Closed is not null && Closed.ExpectedCash.Amount != ExpectedCash.Amount;
}

/// <param name="At">In the salon's zone.</param>
/// <param name="Amount">Signed: above zero came in, below went out.</param>
/// <param name="AppointmentId">The visit the money is about, for a link.</param>
public sealed record CashMovement(
    ZonedDateTime At,
    ClientAccountEntryKind Kind,
    PaymentMethod Method,
    Money Amount,
    string ClientName,
    Guid? AppointmentId,
    string? ByName)
{
    /// <summary>The name of the salon's own method on the day of the line (ADR 0060); null for a built-in one.</summary>
    public string? MethodName { get; init; }
}

/// <summary>What came in less what went out by one method; each of the salon's own methods is a line of its own (ADR 0060).</summary>
public sealed record MethodTotal(PaymentMethod Method, Money Amount)
{
    /// <summary>Which of the salon's own methods; null for a built-in one.</summary>
    public Guid? CustomMethodId { get; init; }

    /// <summary>Its name on its latest line; null for a built-in one.</summary>
    public string? MethodName { get; init; }

    /// <summary>What tells this line from the others: the built-in method's name, or the id of the salon's own.</summary>
    public string Key => new PaymentChoice(Method, CustomMethodId).Value;
}

/// <param name="Difference">Counted less expected: above zero is money over, below is money short.</param>
/// <param name="ClosedAt">In the salon's zone.</param>
public sealed record CashUpRecord(Money ExpectedCash, Money CountedCash, Money Difference, string? Note, ZonedDateTime ClosedAt, string? ByName);

public enum CashUpOutcome
{
    Closed = 1,

    /// <summary>Somebody closed this day already; the first count stands.</summary>
    AlreadyClosed = 2,

    /// <summary>The day has not come yet.</summary>
    FutureDay = 3,
}

public sealed record CashUpResult(CashUpOutcome Outcome, CashDay Day);