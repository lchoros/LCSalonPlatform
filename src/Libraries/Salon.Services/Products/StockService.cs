using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Products;
using Salon.Data;

namespace Salon.Services.Products;

/// <summary>
/// Reads the product through the tenant filter, decides with <see cref="StockRules"/> and saves the line and the new
/// total in one go. The product's row version (xmin) makes the slower of two simultaneous changes fail instead of
/// overwriting the faster one's total.
/// </summary>
internal sealed class StockService(SalonDbContext db, IClock clock, ILogger<StockService> logger) : IStockService
{
    public async Task<StockResult> RecordAsync(Guid productId, StockMovementKind kind, int number, string? note, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return new StockResult(StockOutcome.ProductNotFound);
        }

        var onHand = product.StockOnHand;
        var isCount = kind == StockMovementKind.Counted;
        if (!Enum.IsDefined(kind) || (isCount ? !StockRules.IsCountable(number) : !StockRules.IsMovable(number)))
        {
            return new StockResult(StockOutcome.InvalidNumber, onHand);
        }

        var remark = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (remark is null && StockRules.NeedsNote(kind))
        {
            return new StockResult(StockOutcome.NoteRequired, onHand);
        }

        var change = StockRules.Change(kind, number, onHand);
        if (change == 0)
        {
            return new StockResult(StockOutcome.CountMatches, onHand);
        }

        if (!StockRules.Fits(onHand, change))
        {
            return new StockResult(change < 0 ? StockOutcome.NotEnough : StockOutcome.TooMany, onHand);
        }

        var movement = new StockMovement
        {
            TenantId = product.TenantId,
            ProductId = product.Id,
            Kind = kind,
            Quantity = change,
            OnHandAfter = onHand + change,
            Note = remark is { Length: > StockMovement.NoteMaxLength } ? remark[..StockMovement.NoteMaxLength] : remark,
            At = clock.GetCurrentInstant(),
            ByUserId = byUserId,
        };
        product.StockOnHand = movement.OnHandAfter;
        db.StockMovements.Add(movement);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Somebody else moved this shelf between our read and our save: their line stands, ours is dropped.
            db.Entry(movement).State = EntityState.Detached;
            await db.Entry(product).ReloadAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(ex, "Stock of product {ProductId} changed during a {Kind} by user {UserId}; nothing recorded", productId, kind, byUserId);
            return new StockResult(StockOutcome.ChangedMeanwhile, product.StockOnHand);
        }

        logger.LogInformation("Stock of product {ProductId}: {Kind} {Change} by user {UserId}, now {OnHand}", productId, kind, change, byUserId, movement.OnHandAfter);
        return new StockResult(StockOutcome.Recorded, movement.OnHandAfter, change);
    }
}