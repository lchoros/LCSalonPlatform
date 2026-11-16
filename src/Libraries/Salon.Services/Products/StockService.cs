using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Payments;
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
    /// <summary>A sale is part of a payment and must not fail for a busy shelf; three tries outlast any real desk.</summary>
    private const int SaleAttempts = 3;

    public async Task<StockResult> RecordAsync(Guid productId, StockMovementKind kind, int number, string? note, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        if (kind is StockMovementKind.Sold or StockMovementKind.Returned)
        {
            throw new ArgumentException("A sale and a return belong to a bill; use SellAsync or ReturnAsync.", nameof(kind));
        }

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

        var lines = new[] { Line(product, kind, change, onHand + change, remark, null, byUserId) };
        return await SaveAsync(product, lines, cancellationToken).ConfigureAwait(false)
            ? new StockResult(StockOutcome.Recorded, product.StockOnHand, change)
            : new StockResult(StockOutcome.ChangedMeanwhile, product.StockOnHand);
    }

    public async Task<StockResult> SellAsync(Guid productId, int quantity, Guid? appointmentId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return new StockResult(StockOutcome.ProductNotFound);
        }

        if (!ProductSaleRules.IsQuantity(quantity))
        {
            return new StockResult(StockOutcome.InvalidNumber, product.StockOnHand);
        }

        for (var attempt = 1; attempt <= SaleAttempts; attempt++)
        {
            var onHand = product.StockOnHand;
            var lines = new List<StockMovement>(2);

            // The record holds fewer than the client is taking home: the units exist, so the record was short.
            var found = StockRules.ShortFor(onHand, quantity);
            if (found > 0)
            {
                lines.Add(Line(product, StockMovementKind.Counted, found, onHand + found, null, appointmentId, byUserId));
            }

            // One microsecond later than the count, so the history always reads count first, sale second.
            var sold = Line(product, StockMovementKind.Sold, -quantity, onHand + found - quantity, null, appointmentId, byUserId);
            sold.At = sold.At.Plus(Duration.FromTicks(10 * lines.Count));
            lines.Add(sold);
            if (await SaveAsync(product, lines, cancellationToken).ConfigureAwait(false))
            {
                if (found > 0)
                {
                    logger.LogInformation("Sale of product {ProductId} on appointment {AppointmentId}: the record was {Found} short and was counted up first", productId, appointmentId, found);
                }

                return new StockResult(StockOutcome.Recorded, product.StockOnHand, -quantity);
            }
        }

        return new StockResult(StockOutcome.ChangedMeanwhile, product.StockOnHand);
    }

    public async Task<StockResult> ReturnAsync(Guid productId, int quantity, Guid? appointmentId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null)
        {
            return new StockResult(StockOutcome.ProductNotFound);
        }

        if (!ProductSaleRules.IsQuantity(quantity))
        {
            return new StockResult(StockOutcome.InvalidNumber, product.StockOnHand);
        }

        for (var attempt = 1; attempt <= SaleAttempts; attempt++)
        {
            var onHand = product.StockOnHand;
            if (!StockRules.Fits(onHand, quantity))
            {
                return new StockResult(StockOutcome.TooMany, onHand);
            }

            var lines = new[] { Line(product, StockMovementKind.Returned, quantity, onHand + quantity, null, appointmentId, byUserId) };
            if (await SaveAsync(product, lines, cancellationToken).ConfigureAwait(false))
            {
                return new StockResult(StockOutcome.Recorded, product.StockOnHand, quantity);
            }
        }

        return new StockResult(StockOutcome.ChangedMeanwhile, product.StockOnHand);
    }

    private StockMovement Line(Product product, StockMovementKind kind, int change, int onHandAfter, string? note, Guid? appointmentId, Guid? byUserId)
        => new()
        {
            TenantId = product.TenantId,
            ProductId = product.Id,
            Kind = kind,
            Quantity = change,
            OnHandAfter = onHandAfter,
            Note = note is { Length: > StockMovement.NoteMaxLength } ? note[..StockMovement.NoteMaxLength] : note,
            AppointmentId = appointmentId,
            At = clock.GetCurrentInstant(),
            ByUserId = byUserId,
        };

    /// <summary>
    /// Saves the lines and the total they lead to. False when somebody else moved this shelf between our read and our
    /// save: their line stands, ours are dropped and the product is read again, so the caller sees the shelf as it is.
    /// </summary>
    private async Task<bool> SaveAsync(Product product, IReadOnlyList<StockMovement> lines, CancellationToken cancellationToken)
    {
        product.StockOnHand = lines[^1].OnHandAfter;
        db.StockMovements.AddRange(lines);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var line in lines)
            {
                db.Entry(line).State = EntityState.Detached;
            }

            await db.Entry(product).ReloadAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation(ex, "Stock of product {ProductId} changed during a {Kind}; nothing recorded", product.Id, lines[^1].Kind);
            return false;
        }

        foreach (var line in lines)
        {
            logger.LogInformation("Stock of product {ProductId}: {Kind} {Change} by user {UserId}, now {OnHand}", product.Id, line.Kind, line.Quantity, line.ByUserId, line.OnHandAfter);
        }

        return true;
    }
}