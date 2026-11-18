using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Tenancy;

namespace Salon.Services.Payments;

/// <param name="Day">The day the sales are of, on the salon's calendar.</param>
/// <param name="Today">Today on the salon's calendar.</param>
public sealed record CounterSaleDay(LocalDate Day, LocalDate Today, IReadOnlyList<CounterSaleRecord> Sales);

/// <summary>One sale taken at the counter, as the desk reads it back (ADR 0054).</summary>
/// <param name="At">In the salon's zone.</param>
/// <param name="ByName">Who took it; null when the login is gone or unknown.</param>
/// <param name="Returned">Money given back for it so far.</param>
public sealed record CounterSaleRecord(Guid Id, ZonedDateTime At, PaymentMethod Method, string? ByName, Money Total, Money Returned, IReadOnlyList<CounterSaleRecordLine> Lines)
{
    public bool HasReturns => Returned.Amount > 0m;

    /// <summary>Which of the salon's own methods it was paid by, with its name on that day (ADR 0060); null for a built-in one.</summary>
    public Guid? CustomMethodId { get; init; }

    public string? MethodName { get; init; }

    /// <summary>How it was paid, as a form offers it: the money goes back the same way unless the desk says otherwise.</summary>
    public PaymentChoice PaidWith => new(Method, CustomMethodId);
}

/// <param name="ReturnedQuantity">Units brought back so far.</param>
public sealed record CounterSaleRecordLine(Guid Id, string Name, int Quantity, int ReturnedQuantity, Money UnitPrice)
{
    /// <summary>Units the buyer still has: what can still come back.</summary>
    public int Kept => Quantity - ReturnedQuantity;

    public bool CanReturn => Kept > 0;

    public bool HasReturns => ReturnedQuantity > 0;
}

/// <summary>Reading counter sales back and giving money back for them (ADR 0054).</summary>
internal sealed partial class CounterSaleService
{
    public async Task<CounterSaleDay> ListAsync(LocalDate? day, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Counter sales belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var shown = day ?? today;
        var start = zone.AtStartOfDay(shown).ToInstant();
        var end = zone.AtStartOfDay(shown.PlusDays(1)).ToInstant();

        // A counter sale is the product sale that belongs to no visit and no client (ADR 0053).
        var sales = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.Kind == ClientAccountEntryKind.ProductSale && e.ClientId == null && e.AppointmentId == null && e.At >= start && e.At < end)
            .OrderByDescending(e => e.At).ThenByDescending(e => e.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var ids = sales.Select(s => s.Id).ToArray();
        var lines = (await db.CounterSaleLines.AsNoTracking()
                .Where(l => ids.Contains(l.PaymentEntryId))
                .OrderBy(l => l.ProductName).ThenBy(l => l.Id)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToLookup(l => l.PaymentEntryId);
        var returned = (await db.ClientAccountEntries.AsNoTracking()
                .Where(e => e.Kind == ClientAccountEntryKind.ProductReturn && e.RelatedEntryId != null && ids.Contains(e.RelatedEntryId.Value))
                .Select(e => new { SaleId = e.RelatedEntryId!.Value, e.Amount })
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToLookup(r => r.SaleId, r => r.Amount);
        var userIds = sales.Where(s => s.ByUserId is not null).Select(s => s.ByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken).ConfigureAwait(false);

        // A sale recorded under the wrong method and put right reads as it was paid (ADR 0064).
        var standsAt = await MethodCorrections.LatestAsync(db, ids, cancellationToken).ConfigureAwait(false);
        var records = sales
            .Select(s => new CounterSaleRecord(
                s.Id,
                s.At.InZone(zone),
                standsAt.Of(s.Id, s.Method, s.CustomMethodId, s.MethodName)?.Method ?? PaymentMethod.Cash,
                s.ByUserId is { } userId && users.TryGetValue(userId, out var name) ? name : null,
                s.Money,
                new Money(returned[s.Id].Sum(), s.Currency),
                [.. lines[s.Id].Select(l => new CounterSaleRecordLine(l.Id, l.ProductName, l.Quantity, l.ReturnedQuantity, new Money(l.UnitPrice, l.Currency)))])
            {
                CustomMethodId = standsAt.Of(s.Id, s.Method, s.CustomMethodId, s.MethodName)?.CustomMethodId,
                MethodName = standsAt.Of(s.Id, s.Method, s.CustomMethodId, s.MethodName)?.MethodName,
            })
            .ToArray();
        return new CounterSaleDay(shown, today, records);
    }

    public async Task<Money> ReturnAsync(Guid saleId, Guid lineId, int quantity, PaymentChoice method, bool backOnShelf, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A counter sale belongs to a salon, but no tenant is set in the current scope.");
        var line = await db.CounterSaleLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId && l.PaymentEntryId == saleId, cancellationToken).ConfigureAwait(false)
            ?? throw new CounterSaleException(CounterSaleRefusal.SaleNotFound);
        if (!ProductSaleRules.CanReturn(true, line.Quantity, line.ReturnedQuantity, quantity))
        {
            throw new CounterSaleException(CounterSaleRefusal.ReturnMoreThanKept);
        }

        var paid = await methods.ResolveAsync(method, cancellationToken).ConfigureAwait(false) ?? throw new CounterSaleException(CounterSaleRefusal.MethodNotOffered);

        // The units are claimed in one statement that only succeeds while the buyer still has them, so two requests
        // for the last unit (a double click, two tills) cannot both get the money back.
        var claimed = await db.CounterSaleLines
            .Where(l => l.Id == lineId && l.Quantity - l.ReturnedQuantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReturnedQuantity, l => l.ReturnedQuantity + quantity), cancellationToken).ConfigureAwait(false);
        if (claimed != 1)
        {
            throw new CounterSaleException(CounterSaleRefusal.ReturnMoreThanKept);
        }

        // The money goes back for what was paid: the price the line was sold at, not today's price.
        var back = new Money(ProductSaleRules.LineTotal(quantity, line.UnitPrice), line.Currency);
        var result = await payments.RecordRefundAsync(new PaymentRequest(tenantId, null, back, paid.Method), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            // The caller's transaction takes the claim back with it.
            logger.LogWarning("Payment plugin {Provider} did not record the money given back for counter sale {SaleId}: {Error}", payments.SystemName, saleId, result.Error);
            throw new CounterSaleException(CounterSaleRefusal.PaymentNotRecorded);
        }

        db.ClientAccountEntries.Add(new ClientAccountEntry
        {
            TenantId = tenantId,
            ClientId = null,
            AppointmentId = null,
            Kind = ClientAccountEntryKind.ProductReturn,
            Amount = back.Amount,
            Currency = back.Currency,
            Method = paid.Method,
            CustomMethodId = paid.CustomMethodId,
            MethodName = paid.MethodName,
            ProviderSystemName = payments.SystemName,
            ProviderReference = result.ProviderReference,
            RelatedEntryId = saleId,
            ByUserId = byUserId,
            At = clock.GetCurrentInstant(),
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A bottle that can be sold again goes back on its shelf; an opened or broken one does not come back at all.
        if (backOnShelf)
        {
            var stocked = await stock.ReturnAsync(line.ProductId, quantity, null, byUserId, cancellationToken).ConfigureAwait(false);
            if (!stocked.IsRecorded)
            {
                logger.LogWarning("Product {ProductId} returned from counter sale {SaleId} could not go back on its shelf: {Outcome}", line.ProductId, saleId, stocked.Outcome);
                throw new CounterSaleException(CounterSaleRefusal.StockNotRecorded);
            }
        }

        logger.LogInformation("Counter sale {SaleId} line {LineId}: {Quantity} returned, {Amount} {Currency} back by {Method}, on the shelf again: {BackOnShelf}, recorded by user {UserId}", saleId, lineId, quantity, back.Amount, back.Currency, paid.Method, backOnShelf, byUserId);
        return back;
    }
}