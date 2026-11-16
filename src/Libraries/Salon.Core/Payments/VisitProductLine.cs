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

    /// <summary>
    /// What one cost the salon on the day it was paid for (ADR 0052), copied from the product then; null while the
    /// line is unpaid, and for a sale made when nobody had said what the product costs. Never changes afterwards, so
    /// the profit of a past sale does not move when the supplier changes the price.
    /// </summary>
    public decimal? UnitCost { get; set; }

    /// <summary>ISO 4217 code of <see cref="UnitPrice"/>.</summary>
    public required string Currency { get; set; }

    public Instant AddedAt { get; set; }

    /// <summary>The login that put it on the bill.</summary>
    public Guid? AddedByUserId { get; set; }

    /// <summary>The account line that paid for it; null while it is unpaid.</summary>
    public Guid? PaymentEntryId { get; set; }

    /// <summary>
    /// Units of a paid line the client brought back and was given the money for (ADR 0040); never more than
    /// <see cref="Quantity"/>. The one thing about a paid line that changes, and only upwards.
    /// </summary>
    public int ReturnedQuantity { get; set; }

    public bool IsPaid => PaymentEntryId is not null;

    /// <summary>Units the client still has.</summary>
    public int Kept => Quantity - ReturnedQuantity;

    public Money Total => new(ProductSaleRules.LineTotal(Quantity, UnitPrice), Currency);
}

/// <param name="Amount">Price less cost over the kept units that have a cost; null when none has one. Below zero for a loss.</param>
/// <param name="Percent">Of what those units were sold for, one decimal; null without <paramref name="Amount"/> or when they were given away.</param>
/// <param name="UnitsWithoutCost">Kept units sold with no cost on record; they are in neither figure.</param>
public readonly record struct RetailProfit(decimal? Amount, decimal? Percent, int UnitsWithoutCost);

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

    /// <summary>
    /// What the salon kept of the products it sold (ADR 0052): for every line with a cost on record, the price less
    /// the cost, times the units the client still has. Units brought back earn nothing. Lines without a cost are not
    /// guessed at: their kept units are counted so the figure is not taken for the whole.
    /// </summary>
    public static RetailProfit Profit(IEnumerable<(int Kept, decimal UnitPrice, decimal? UnitCost)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var profit = 0m;
        var revenue = 0m;
        var withoutCost = 0;
        var costed = 0;
        foreach (var (kept, unitPrice, unitCost) in lines)
        {
            if (kept <= 0)
            {
                continue;
            }

            if (unitCost is not { } cost)
            {
                withoutCost += kept;
                continue;
            }

            costed += kept;
            revenue += kept * unitPrice;
            profit += kept * (unitPrice - cost);
        }

        return new RetailProfit(
            costed > 0 ? profit : null,
            costed > 0 && revenue > 0m ? decimal.Round(100m * profit / revenue, 1, MidpointRounding.AwayFromZero) : null,
            withoutCost);
    }

    /// <summary>What is left to pay for products: the lines nobody has paid for yet.</summary>
    public static decimal Outstanding(IEnumerable<(int Quantity, decimal UnitPrice, bool IsPaid)> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Where(l => !l.IsPaid).Sum(l => LineTotal(l.Quantity, l.UnitPrice));
    }

    /// <summary>
    /// A return (ADR 0040) is for a line that was paid, of at least one unit and no more than the client still has.
    /// </summary>
    public static bool CanReturn(bool isPaid, int quantity, int alreadyReturned, int units)
        => isPaid && units > 0 && units <= quantity - alreadyReturned;
}