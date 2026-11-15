using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Products;
using Salon.Core.Tenancy;

namespace Salon.Core.Payments;

/// <summary>
/// A product on the bill of a visit (ADR 0037): which product, how many and at what price each. Tenant-owned. The name
/// and the price are copied when the line is added, so a bill reads the same after the product is renamed or repriced.
/// While <see cref="PaymentEntryId"/> is null the line is only what the desk has rung up: its quantity may change and
/// it may be taken off again. Once paid it points at the <see cref="ClientAccountEntryKind.ProductSale"/> line that
/// settled it and never changes: the money and the stock have moved.
/// </summary>
public class VisitProductLine : BaseEntity, ITenantEntity
{
    public const int CurrencyLength = 3;

    public Guid TenantId { get; set; }

    public Guid AppointmentId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>The product's name when it was put on the bill.</summary>
    public required string ProductName { get; set; }

    /// <summary>Whole units, above zero.</summary>
    public int Quantity { get; set; }

    /// <summary>The price of one when it was put on the bill.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>ISO 4217 code of <see cref="UnitPrice"/>.</summary>
    public required string Currency { get; set; }

    public Instant AddedAt { get; set; }

    /// <summary>The login that put it on the bill.</summary>
    public Guid? AddedByUserId { get; set; }

    /// <summary>The account line that paid for it; null while it is unpaid.</summary>
    public Guid? PaymentEntryId { get; set; }

    public bool IsPaid => PaymentEntryId is not null;

    public Money Total => new(ProductSaleRules.LineTotal(Quantity, UnitPrice), Currency);
}

/// <summary>What may go on a bill and how its lines add up (ADR 0037). Pure.</summary>
public static class ProductSaleRules
{
    /// <summary>More than any client carries out of a salon; keeps a slip of the finger off the bill.</summary>
    public const int MaxQuantity = 99;

    public static bool IsQuantity(int quantity) => quantity is > 0 and <= MaxQuantity;

    /// <summary>Only a product the salon still uses and has priced above zero is sold; one without a price is for the chair.</summary>
    public static bool IsForSale(Product product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return product.IsActive && product.RetailPrice is > 0m;
    }

    public static decimal LineTotal(int quantity, decimal unitPrice) => quantity * unitPrice;

    /// <summary>What is left to pay for products: the lines nobody has paid for yet.</summary>
    public static decimal Outstanding(IEnumerable<(int Quantity, decimal UnitPrice, bool IsPaid)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Where(l => !l.IsPaid).Sum(l => LineTotal(l.Quantity, l.UnitPrice));
    }
}