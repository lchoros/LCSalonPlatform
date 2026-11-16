using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Payments;

namespace Salon.Web.Features.Payments.CounterSale;

/// <summary>One row of the form: a product and how many. A row left empty is no row.</summary>
public sealed class CounterSaleRow
{
    public Guid? Product { get; set; }

    public int? Quantity { get; set; }
}

/// <summary>
/// The counter sale form (ADR 0053): the products the salon sells and, when rows were chosen, what they come to or why
/// they cannot be sold. A query: no transaction, tenant required.
/// </summary>
public sealed record CounterSaleFormQuery(IReadOnlyList<CounterSaleRow> Rows) : IQuery<CounterSaleForm>;

/// <param name="Rows">The rows as chosen, padded to <see cref="RowCount"/> so the form always has room.</param>
/// <param name="Quote">What the chosen rows come to; null when nothing is chosen or it cannot be sold.</param>
/// <param name="Refusal">Why the chosen rows cannot be sold; null when they can, or nothing is chosen.</param>
public sealed record CounterSaleForm(
    IReadOnlyList<SellableProduct> Products,
    IReadOnlyList<CounterSaleRow> Rows,
    CounterSaleQuote? Quote,
    CounterSaleRefusal? Refusal)
{
    /// <summary>Rows on the form; a sale of more different products is taken as two.</summary>
    public const int RowCount = 5;

    public bool HasProducts => Products.Count > 0;
}

/// <summary>Records a counter sale (ADR 0053). A command: runs in the pipeline's transaction, so the money, the lines and the stock commit together.</summary>
/// <param name="SaleId">The id the form was given when the total was shown; the same form sent twice is one sale.</param>
public sealed record RecordCounterSaleCommand(IReadOnlyList<CounterSaleRow> Rows, PaymentMethod Method, Guid? ByUserId, Guid? SaleId = null) : ICommand<CounterSaleReceipt>;

public sealed class CounterSaleFormHandler(ICounterSaleService sales) : IRequestHandler<CounterSaleFormQuery, CounterSaleForm>
{
    public async Task<CounterSaleForm> HandleAsync(CounterSaleFormQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var products = await sales.ListSellableAsync(cancellationToken);
        var rows = request.Rows.Take(CounterSaleForm.RowCount)
            .Concat(Enumerable.Range(0, CounterSaleForm.RowCount).Select(_ => new CounterSaleRow()))
            .Take(CounterSaleForm.RowCount)
            .ToArray();
        var lines = CounterSaleRows.Lines(request.Rows);
        if (lines.Count == 0)
        {
            return new CounterSaleForm(products, rows, null, null);
        }

        try
        {
            return new CounterSaleForm(products, rows, await sales.QuoteAsync(lines, cancellationToken), null);
        }
        catch (CounterSaleException ex)
        {
            return new CounterSaleForm(products, rows, null, ex.Refusal);
        }
    }
}

/// <summary>Hands the sale to <see cref="ICounterSaleService"/>, which owns it; nothing about money is decided in a slice.</summary>
public sealed class RecordCounterSaleHandler(ICounterSaleService sales) : IRequestHandler<RecordCounterSaleCommand, CounterSaleReceipt>
{
    public Task<CounterSaleReceipt> HandleAsync(RecordCounterSaleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return sales.SellAsync(CounterSaleRows.Lines(request.Rows), request.Method, request.ByUserId, request.SaleId, cancellationToken);
    }
}

internal static class CounterSaleRows
{
    public static IReadOnlyList<(Guid ProductId, int Quantity)> Lines(IReadOnlyList<CounterSaleRow> rows)
        => CounterSaleRules.Lines(rows.Select(r => (r.Product, r.Quantity)));
}