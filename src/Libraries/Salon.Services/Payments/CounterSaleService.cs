using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Products;

namespace Salon.Services.Payments;

/// <summary>A product the desk can sell at the counter, as the form lists it.</summary>
public sealed record SellableProduct(Guid Id, string Name, Money Price, int OnHand);

/// <summary>One line of a counter sale as it will be, or was, recorded.</summary>
public sealed record CounterSaleQuoteLine(Guid ProductId, string Name, int Quantity, Money UnitPrice, Money Total);

/// <summary>What a counter sale comes to before it is recorded.</summary>
public sealed record CounterSaleQuote(IReadOnlyList<CounterSaleQuoteLine> Lines, Money Total)
{
    public int Units => Lines.Sum(l => l.Quantity);
}

/// <param name="EntryId">The account line that is the sale.</param>
public sealed record CounterSaleReceipt(Guid EntryId, Money Total, int Units, PaymentMethod Method);

public enum CounterSaleRefusal
{
    /// <summary>No product was chosen.</summary>
    NothingChosen,

    /// <summary>A chosen product is not one the salon sells: unknown, not in use, or without a price.</summary>
    ProductNotForSale,

    /// <summary>A quantity is not between one and <see cref="ProductSaleRules.MaxQuantity"/>.</summary>
    QuantityOutOfRange,

    /// <summary>More different products than <see cref="CounterSaleRules.MaxLines"/>.</summary>
    TooManyLines,

    /// <summary>The payment plugin did not record the money.</summary>
    PaymentNotRecorded,
}

public sealed class CounterSaleException : Exception
{
    public CounterSaleException()
        : this(CounterSaleRefusal.NothingChosen)
    {
    }

    public CounterSaleException(string message)
        : base(message)
    {
    }

    public CounterSaleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public CounterSaleException(CounterSaleRefusal refusal)
        : base($"The counter sale was refused: {refusal}.")
    {
        Refusal = refusal;
    }

    public CounterSaleRefusal Refusal { get; }
}

/// <summary>
/// Products sold at the counter without a visit (ADR 0053): somebody walks in, buys a bottle and leaves. The money is
/// one <see cref="ClientAccountEntryKind.ProductSale"/> line with no client and no visit, so the till, the takings and
/// the reports count it like any other product sale.
/// </summary>
public interface ICounterSaleService
{
    /// <summary>The products the salon sells, by name.</summary>
    Task<IReadOnlyList<SellableProduct>> ListSellableAsync(CancellationToken cancellationToken = default);

    /// <summary>What the chosen products come to. Throws <see cref="CounterSaleException"/> with the reason when they cannot be sold.</summary>
    Task<CounterSaleQuote> QuoteAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the sale: the money through the payment plugin, one account line, a line per product with its price and
    /// cost of the day, and every unit off its shelf. All of it or none of it: the caller's transaction decides.
    /// Throws <see cref="CounterSaleException"/> with the reason when nothing was recorded.
    /// </summary>
    Task<CounterSaleReceipt> SellAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default);
}

internal sealed class CounterSaleService(
    SalonDbContext db,
    ITenantContext tenantContext,
    IPaymentProvider payments,
    IStockService stock,
    IClock clock,
    ILogger<CounterSaleService> logger) : ICounterSaleService
{
    public async Task<IReadOnlyList<SellableProduct>> ListSellableAsync(CancellationToken cancellationToken = default)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive && p.RetailPrice > 0m).ToListAsync(cancellationToken).ConfigureAwait(false);
        return [.. products
            .Where(ProductSaleRules.IsForSale)
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Id)
            .Select(p => new SellableProduct(p.Id, p.Name, p.Price!.Value, p.StockOnHand))];
    }

    public async Task<CounterSaleQuote> QuoteAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, CancellationToken cancellationToken = default)
        => (await PriceAsync(lines, cancellationToken).ConfigureAwait(false)).Quote;

    public async Task<CounterSaleReceipt> SellAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A counter sale belongs to a salon, but no tenant is set in the current scope.");
        var (quote, products) = await PriceAsync(lines, cancellationToken).ConfigureAwait(false);

        // Phase 1: the plugin only records that the salon took the money itself (ADR 0024). No visit, so no appointment.
        var result = await payments.RecordInSalonPaymentAsync(new PaymentRequest(tenantId, null, quote.Total, method), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            logger.LogWarning("Payment plugin {Provider} did not record the money for a counter sale: {Error}", payments.SystemName, result.Error);
            throw new CounterSaleException(CounterSaleRefusal.PaymentNotRecorded);
        }

        var sale = new ClientAccountEntry
        {
            TenantId = tenantId,
            ClientId = null,
            AppointmentId = null,
            Kind = ClientAccountEntryKind.ProductSale,
            Amount = quote.Total.Amount,
            Currency = quote.Total.Currency,
            Method = method,
            ProviderSystemName = payments.SystemName,
            ProviderReference = result.ProviderReference,
            ByUserId = byUserId,
            At = clock.GetCurrentInstant(),
        };
        db.ClientAccountEntries.Add(sale);
        foreach (var line in quote.Lines)
        {
            db.CounterSaleLines.Add(new CounterSaleLine
            {
                TenantId = tenantId,
                PaymentEntryId = sale.Id,
                ProductId = line.ProductId,
                ProductName = line.Name,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice.Amount,
                // The cost of the day stays with the sale (ADR 0052).
                UnitCost = products[line.ProductId].CostPrice,
                Currency = line.UnitPrice.Currency,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var line in quote.Lines)
        {
            var stocked = await stock.SellAsync(line.ProductId, line.Quantity, null, byUserId, cancellationToken).ConfigureAwait(false);
            if (!stocked.IsRecorded)
            {
                // The caller's transaction takes the money line back with it; nothing is half sold.
                throw new InvalidOperationException($"The stock of product {line.ProductId} was not updated for counter sale {sale.Id}: {stocked.Outcome}.");
            }
        }

        logger.LogInformation("Counter sale {EntryId}: {Units} units for {Amount} {Currency} by {Method}, recorded by user {UserId} through {Provider}", sale.Id, quote.Units, quote.Total.Amount, quote.Total.Currency, method, byUserId, payments.SystemName);
        return new CounterSaleReceipt(sale.Id, quote.Total, quote.Units, method);
    }

    private async Task<(CounterSaleQuote Quote, Dictionary<Guid, Product> Products)> PriceAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new CounterSaleException(CounterSaleRefusal.NothingChosen);
        }

        if (lines.Count > CounterSaleRules.MaxLines || lines.Select(l => l.ProductId).Distinct().Count() != lines.Count)
        {
            throw new CounterSaleException(CounterSaleRefusal.TooManyLines);
        }

        if (lines.Any(l => !ProductSaleRules.IsQuantity(l.Quantity)))
        {
            throw new CounterSaleException(CounterSaleRefusal.QuantityOutOfRange);
        }

        var ids = lines.Select(l => l.ProductId).ToArray();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken).ConfigureAwait(false);
        if (lines.Any(l => !products.TryGetValue(l.ProductId, out var product) || !ProductSaleRules.IsForSale(product)))
        {
            throw new CounterSaleException(CounterSaleRefusal.ProductNotForSale);
        }

        var quoted = lines
            .Select(l =>
            {
                var product = products[l.ProductId];
                var price = product.Price!.Value;
                return new CounterSaleQuoteLine(product.Id, product.Name, l.Quantity, price, price with { Amount = ProductSaleRules.LineTotal(l.Quantity, price.Amount) });
            })
            .ToArray();
        var total = new Money(quoted.Sum(l => l.Total.Amount), quoted[0].Total.Currency);
        return (new CounterSaleQuote(quoted, total), products);
    }
}