using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Products;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Products;
using Salon.Services.Tenancy;

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

    /// <summary>The same form is being recorded by another request at this moment (a double click, two tills).</summary>
    AlreadyBeingRecorded,

    /// <summary>The salon has no such counter sale, or the sale has no such line.</summary>
    SaleNotFound,

    /// <summary>Not a quantity, or more than the buyer still has of that line.</summary>
    ReturnMoreThanKept,

    /// <summary>The units could not be put back on the shelf.</summary>
    StockNotRecorded,
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
    /// <paramref name="saleId"/> is the form's own id for the sale: sending the same form again records nothing more
    /// and answers with the sale that was recorded the first time, so a double click cannot sell twice.
    /// </summary>
    Task<CounterSaleReceipt> SellAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, PaymentMethod method, Guid? byUserId, Guid? saleId = null, CancellationToken cancellationToken = default);

    /// <summary>The sales taken at the counter on <paramref name="day"/> in the salon's zone (today when null), newest first.</summary>
    Task<CounterSaleDay> ListAsync(LocalDate? day, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives the money back for units of one line of a counter sale (ADR 0054): at the price they were sold at, as one
    /// <see cref="ClientAccountEntryKind.ProductReturn"/> line that points at the sale, and back on the shelf only when
    /// they can be sold again. Two requests for the last unit cannot both succeed. Throws
    /// <see cref="CounterSaleException"/> with the reason when nothing was recorded.
    /// </summary>
    Task<Money> ReturnAsync(Guid saleId, Guid lineId, int quantity, PaymentMethod method, bool backOnShelf, Guid? byUserId, CancellationToken cancellationToken = default);
}

internal sealed partial class CounterSaleService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
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

    public async Task<CounterSaleReceipt> SellAsync(IReadOnlyList<(Guid ProductId, int Quantity)> lines, PaymentMethod method, Guid? byUserId, Guid? saleId = null, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A counter sale belongs to a salon, but no tenant is set in the current scope.");
        if (saleId is { } sent && await RecordedAsync(sent, cancellationToken).ConfigureAwait(false) is { } recorded)
        {
            // The same form again (a second click, a reload of the post): nothing more is sold.
            return recorded;
        }

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
        if (saleId is { } chosen)
        {
            // The form's id is the line's id, so the database itself refuses the same sale twice.
            sale.Id = chosen;
        }

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

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request with the same form got there first; the caller's transaction takes this one back.
            throw new CounterSaleException(CounterSaleRefusal.AlreadyBeingRecorded);
        }

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

    /// <summary>The sale already recorded under this id, if there is one in this salon; null otherwise.</summary>
    private async Task<CounterSaleReceipt?> RecordedAsync(Guid saleId, CancellationToken cancellationToken)
    {
        var entry = await db.ClientAccountEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == saleId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        if (entry is not { Kind: ClientAccountEntryKind.ProductSale, ClientId: null, AppointmentId: null, Method: { } method })
        {
            // The id belongs to some other line of money: not a form of ours.
            throw new CounterSaleException(CounterSaleRefusal.AlreadyBeingRecorded);
        }

        var units = await db.CounterSaleLines.AsNoTracking().Where(l => l.PaymentEntryId == saleId).SumAsync(l => l.Quantity, cancellationToken).ConfigureAwait(false);
        return new CounterSaleReceipt(entry.Id, entry.Money, units, method);
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