using Salon.Core.Products;

namespace Salon.Services.Products;

/// <summary>
/// The one way a shelf changes (ADR 0036): every change adds a <see cref="StockMovement"/> line and moves the product's
/// running total with it, together or not at all. A service, not a slice, because more than one slice changes stock
/// (the product page today, the checkout once products are sold there).
/// </summary>
public interface IStockService
{
    /// <summary>
    /// Records <paramref name="number"/> units of <paramref name="kind"/> for the product. For a delivery, a use or a
    /// write-off the number is how many units moved; for <see cref="StockMovementKind.Counted"/> it is what the shelf
    /// holds, and the line is the difference. Nothing is written unless the outcome is
    /// <see cref="StockOutcome.Recorded"/>.
    /// </summary>
    Task<StockResult> RecordAsync(Guid productId, StockMovementKind kind, int number, string? note, Guid? byUserId, CancellationToken cancellationToken = default);
}

public enum StockOutcome
{
    /// <summary>A line was added and the total moved.</summary>
    Recorded,

    /// <summary>The count equals what the lines already say; nothing to put right.</summary>
    CountMatches,

    /// <summary>The salon has no such product.</summary>
    ProductNotFound,

    /// <summary>The number is not a whole number of units the kind accepts.</summary>
    InvalidNumber,

    /// <summary>A write-off needs its reason.</summary>
    NoteRequired,

    /// <summary>More would leave the shelf than the lines say it holds.</summary>
    NotEnough,

    /// <summary>The shelf would hold more than a shelf can on record.</summary>
    TooMany,

    /// <summary>Somebody else changed this product at the same moment; nothing was written.</summary>
    ChangedMeanwhile,
}

/// <param name="OnHand">What the shelf holds according to the lines after the call; the unchanged number when nothing was written.</param>
/// <param name="Change">The signed change that was recorded; 0 when nothing was written.</param>
public sealed record StockResult(StockOutcome Outcome, int OnHand = 0, int Change = 0)
{
    public bool IsRecorded => Outcome == StockOutcome.Recorded;
}