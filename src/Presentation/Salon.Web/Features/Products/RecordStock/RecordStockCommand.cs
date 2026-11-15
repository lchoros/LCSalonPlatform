using Salon.Core.Cqrs;
using Salon.Core.Products;
using Salon.Services.Products;

namespace Salon.Web.Features.Products.RecordStock;

/// <summary>
/// One change to a shelf (ADR 0036): a delivery, units used at the chair, units written off, or a count. A command, so
/// the line and the product's new total commit together in the pipeline's transaction (ADR 0003).
/// </summary>
/// <param name="Number">How many units moved; for a count, how many the shelf holds.</param>
/// <param name="Note">Why units were written off; an optional remark otherwise.</param>
/// <param name="ByUserId">The login that records; set by the controller, never from the form.</param>
public sealed record RecordStockCommand(Guid ProductId, StockMovementKind Kind, int Number, string? Note, Guid? ByUserId) : ICommand<StockResult>;

/// <summary>Hands the change to <see cref="IStockService"/>, which decides and writes; nothing about a shelf is decided in a slice.</summary>
public sealed class RecordStockHandler(IStockService stock) : IRequestHandler<RecordStockCommand, StockResult>
{
    public Task<StockResult> HandleAsync(RecordStockCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return stock.RecordAsync(request.ProductId, request.Kind, request.Number, request.Note, request.ByUserId, cancellationToken);
    }
}