namespace Salon.Core.Products;

/// <summary>
/// How a shelf adds up (ADR 0036). Pure. The desk types a plain number of units; the kind decides whether they come in
/// or go out, and a count is the number the shelf really holds. The shelf never goes below zero: taking more than the
/// lines say is there is refused, and the answer to "but the shelf has more" is a count.
/// </summary>
public static class StockRules
{
    /// <summary>More than any salon moves in one go; keeps a slip of the finger out of the history.</summary>
    public const int MaxQuantity = 10_000;

    /// <summary>The most a shelf is allowed to hold on record.</summary>
    public const int MaxOnHand = 1_000_000;

    /// <summary>A quantity typed for a delivery, a use or a write-off: a whole number of units above zero.</summary>
    public static bool IsMovable(int quantity) => quantity is > 0 and <= MaxQuantity;

    /// <summary>A counted shelf: zero or more.</summary>
    public static bool IsCountable(int counted) => counted is >= 0 and <= MaxOnHand;

    /// <summary>A write-off says why; the other kinds may.</summary>
    public static bool NeedsNote(StockMovementKind kind) => kind == StockMovementKind.WrittenOff;

    /// <summary>+1 for units that come in, -1 for units that go out. A count has no direction of its own.</summary>
    public static int Direction(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Received or StockMovementKind.Returned => 1,
        StockMovementKind.UsedInSalon or StockMovementKind.WrittenOff or StockMovementKind.Sold => -1,
        _ => 0,
    };

    /// <summary>
    /// What a sale needs the shelf to hold first (ADR 0037): nothing when the record covers it, else the units the
    /// record is short. The client is holding them, so the record was wrong and a count puts it right before the sale.
    /// </summary>
    public static int ShortFor(int onHand, int quantity) => Math.Max(0, quantity - Math.Max(0, onHand));

    /// <summary>
    /// The signed change a typed number means for the shelf. For a count the number is what the shelf holds, so the
    /// change is the difference from <paramref name="onHand"/>; it may be zero, which adds no line.
    /// </summary>
    public static int Change(StockMovementKind kind, int number, int onHand)
        => kind == StockMovementKind.Counted ? number - onHand : Direction(kind) * number;

    /// <summary>True when the shelf can give <paramref name="change"/> (below zero) or take it (above zero) and stay within its bounds.</summary>
    public static bool Fits(int onHand, int change) => onHand + (long)change is >= 0 and <= MaxOnHand;

    /// <summary>Low when a threshold is set and the shelf is at or under it.</summary>
    public static bool IsLow(int onHand, int? lowStockAt) => lowStockAt is { } threshold && onHand <= threshold;

    /// <summary>The shelf after a list of signed changes, oldest first.</summary>
    public static int OnHand(IEnumerable<int> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Sum();
    }
}