using System.Diagnostics.CodeAnalysis;
using Salon.Core.Products;

namespace Salon.Web.Framework.Products;

/// <summary>
/// A kind of stock change as it is written in an address (ADR 0036): <c>/admin/products/{id}/stock/received</c>.
/// Here, not in a slice, because the product page builds these addresses and another slice answers them.
/// </summary>
public static class StockKindSegments
{
    public const string Received = "received";
    public const string Used = "used";
    public const string WrittenOff = "written-off";
    public const string Counted = "counted";

    public static string Segment(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Received => Received,
        StockMovementKind.UsedInSalon => Used,
        StockMovementKind.WrittenOff => WrittenOff,
        StockMovementKind.Counted => Counted,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind of stock movement."),
    };

    public static bool TryParse([NotNullWhen(true)] string? segment, out StockMovementKind kind)
    {
        (var known, kind) = segment switch
        {
            Received => (true, StockMovementKind.Received),
            Used => (true, StockMovementKind.UsedInSalon),
            WrittenOff => (true, StockMovementKind.WrittenOff),
            Counted => (true, StockMovementKind.Counted),
            _ => (false, default),
        };
        return known;
    }

    /// <summary>TempData key of the reason a stock change was refused: written by the slice that records, read by the product page.</summary>
    public const string ProblemTempDataKey = "StockProblem";
}