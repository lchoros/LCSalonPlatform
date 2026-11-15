using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Products;

/// <summary>
/// Something the salon keeps on a shelf (ADR 0036): a bottle it sells to clients, a tube of colour it uses up at the
/// chair, or both. Tenant-owned. <see cref="StockOnHand"/> is the running total of the product's
/// <see cref="StockMovement"/> lines and is only ever changed together with one of them (<c>IStockService</c>); the
/// lines are the history, the total is what the shelf should hold now. A product is never deleted, only made inactive,
/// so its history stays readable.
/// </summary>
public class Product : BaseEntity, ITenantEntity
{
    public const int CurrencyLength = 3;

    public Guid TenantId { get; set; }

    /// <summary>What the desk calls it; unique per salon.</summary>
    public required string Name { get; set; }

    /// <summary>The maker or the range; optional.</summary>
    public string? Brand { get; set; }

    /// <summary>The barcode or the supplier's code; optional, for reordering.</summary>
    public string? Code { get; set; }

    /// <summary>What a client pays for one; null for a product the salon only uses itself.</summary>
    public decimal? RetailPrice { get; set; }

    /// <summary>ISO 4217 code of <see cref="RetailPrice"/>; the salon's currency.</summary>
    public required string Currency { get; set; }

    /// <summary>At this many or fewer on the shelf the product shows as low; null never warns.</summary>
    public int? LowStockAt { get; set; }

    /// <summary>Whole units on the shelf according to the lines; never below zero.</summary>
    public int StockOnHand { get; set; }

    public bool IsActive { get; set; } = true;

    public Money? Price => RetailPrice is { } price ? new Money(price, Currency) : null;
}