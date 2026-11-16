using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Payments;

/// <summary>
/// A product sold at the counter without a visit (ADR 0053): somebody walks in, buys a bottle and leaves. Tenant-owned.
/// A counter sale is taken in one go, so every line is paid from the moment it exists: it points at the
/// <see cref="ClientAccountEntryKind.ProductSale"/> line that holds the money, the time, the method and who took it,
/// and never changes. The name, the price and the cost are copied from the product at that moment.
/// </summary>
public class CounterSaleLine : BaseEntity, ITenantEntity
{
    public const int CurrencyLength = 3;

    public Guid TenantId { get; set; }

    /// <summary>The account line that is the sale: one for all its lines.</summary>
    public Guid PaymentEntryId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The product's name when it was sold.</summary>
    public required string ProductName { get; set; }

    /// <summary>Whole units, above zero.</summary>
    public int Quantity { get; set; }

    /// <summary>The price of one when it was sold.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>What one cost the salon that day (ADR 0052); null when nobody had said.</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>ISO 4217 code of <see cref="UnitPrice"/>.</summary>
    public required string Currency { get; set; }

    public Money Total => new(ProductSaleRules.LineTotal(Quantity, UnitPrice), Currency);
}

/// <summary>What a counter sale may hold (ADR 0053). Pure.</summary>
public static class CounterSaleRules
{
    /// <summary>Different products on one sale; a basket bigger than this is two sales.</summary>
    public const int MaxLines = 10;

    /// <summary>
    /// The rows of the form as lines of a sale: rows without a product or with nothing to sell are left out, and a
    /// product chosen twice is one line with the units added up, in the order it was first chosen.
    /// </summary>
    public static IReadOnlyList<(Guid ProductId, int Quantity)> Lines(IEnumerable<(Guid? ProductId, int? Quantity)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var order = new List<Guid>();
        var units = new Dictionary<Guid, int>();
        foreach (var (productId, quantity) in rows)
        {
            if (productId is not { } id || id == Guid.Empty)
            {
                continue;
            }

            // A product chosen with the quantity left empty means one.
            var number = quantity ?? 1;
            if (!units.TryGetValue(id, out var soFar))
            {
                order.Add(id);
            }

            // Kept even when it is out of range, so the caller can refuse it by name instead of dropping it silently.
            units[id] = soFar + number;
        }

        return [.. order.Select(id => (id, units[id]))];
    }
}