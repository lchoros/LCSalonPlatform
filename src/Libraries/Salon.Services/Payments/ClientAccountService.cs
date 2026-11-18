using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Products;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Payments;

/// <summary>
/// Reads and writes through the tenant filter. Lines are only ever inserted; partial unique indexes (one fee, one
/// deposit and one payment per visit, one settlement per fee, one return per deposit) turn a double submit into a
/// refusal instead of a second line (ADR 0022, ADR 0023, ADR 0024).
/// </summary>
internal sealed class ClientAccountService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    ISettingService settings,
    IPaymentProvider payments,
    IPaymentMethodService methods,
    IStockService stock,
    IClock clock,
    ILogger<ClientAccountService> logger) : IClientAccountService
{
    public async Task<AppointmentFee?> GetFeeAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
        var visit = await VisitAsync(appointmentId, zone, cancellationToken).ConfigureAwait(false);
        if (visit is null)
        {
            return null;
        }

        var (fees, deposits, _) = await LinesAsync(db.ClientAccountEntries.Where(e => e.AppointmentId == appointmentId), zone, cancellationToken).ConfigureAwait(false);
        var fee = fees.Count > 0 ? fees[0] : null;
        FeeProposal? proposal = null;
        if (fee is null)
        {
            var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken).ConfigureAwait(false);
            proposal = CancellationPolicy.Propose(visit.Status, visit.Start.ToInstant(), visit.CancelledAt, visit.Price, policy);
        }

        return new AppointmentFee(visit.Id, visit.ClientId, visit.ClientName, visit.ServiceName, visit.StaffName, visit.Start, visit.Status, visit.Price, proposal, fee, Held(deposits)?.Amount);
    }

    public async Task<FeeLine> ChargeFeeAsync(Guid appointmentId, decimal? amount, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var visit = await GetFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (visit.Fee is not null)
        {
            throw new ClientAccountException(ClientAccountRefusal.AlreadyCharged);
        }

        var proposal = visit.Proposal ?? throw new ClientAccountException(ClientAccountRefusal.NoFeeUnderPolicy);

        // A deposit the salon holds for the visit stays with it and counts toward the fee (ADR 0023).
        var chargeable = visit.Chargeable ?? throw new ClientAccountException(ClientAccountRefusal.CoveredByDeposit);
        var value = amount ?? chargeable.Amount;
        if (!IsAmount(value, visit.MaxFee))
        {
            throw new ClientAccountException(ClientAccountRefusal.AmountOutOfRange);
        }

        var entry = new ClientAccountEntry
        {
            TenantId = tenantId,
            ClientId = visit.ClientId,
            AppointmentId = visit.AppointmentId,
            Kind = proposal.Kind,
            Amount = value,
            Currency = visit.Price.Currency,
            ByUserId = byUserId,
        };
        await InsertAsync(entry, ClientAccountRefusal.AlreadyCharged, cancellationToken).ConfigureAwait(false);

        // Ids and the amount only: who owes what is on the account, not in the log.
        logger.LogInformation("{Kind} of {Amount} {Currency} added for appointment {AppointmentId} by user {UserId}", entry.Kind, entry.Amount, entry.Currency, appointmentId, byUserId);
        return await RequireFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeeLine> WaiveFeeAsync(Guid appointmentId, string? reason, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var fee = await RequireOpenFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why))
        {
            throw new ClientAccountException(ClientAccountRefusal.ReasonRequired);
        }

        var entry = Related(tenantId, fee.Visit.ClientId, appointmentId, ClientAccountEntryKind.FeeWaived, fee.Line.Amount, fee.Line.EntryId, byUserId);
        entry.Note = why.Length <= ClientAccountEntry.NoteMaxLength ? why : why[..ClientAccountEntry.NoteMaxLength];
        await InsertAsync(entry, ClientAccountRefusal.AlreadySettled, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Fee {FeeId} of appointment {AppointmentId} waived by user {UserId}", fee.Line.EntryId, appointmentId, byUserId);
        return await RequireFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeeLine> RecordFeePaymentAsync(Guid appointmentId, PaymentChoice method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var paid = await RequireMethodAsync(method, cancellationToken).ConfigureAwait(false);
        var tenantId = RequireTenant();
        var fee = await RequireOpenFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);

        // Phase 1: the plugin only records that the salon took the money itself. An online gateway (M3) must claim the
        // settlement before it charges; see ADR 0022.
        var request = new PaymentRequest(tenantId, appointmentId, fee.Line.Amount, paid.Method, fee.Line.Kind.ToString());
        var result = await RecordAsync(payments.RecordNoShowFeeAsync(request, cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = Related(tenantId, fee.Visit.ClientId, appointmentId, ClientAccountEntryKind.FeePayment, fee.Line.Amount, fee.Line.EntryId, byUserId);
        Stamp(entry, paid);
        entry.ProviderSystemName = payments.SystemName;
        entry.ProviderReference = result.ProviderReference;
        await InsertAsync(entry, ClientAccountRefusal.AlreadySettled, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Fee {FeeId} of appointment {AppointmentId} paid by {Method}, recorded by user {UserId} through {Provider}", fee.Line.EntryId, appointmentId, paid.Method, byUserId, payments.SystemName);
        return await RequireFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VisitDeposit?> GetDepositAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
        var visit = await VisitAsync(appointmentId, zone, cancellationToken).ConfigureAwait(false);
        if (visit is null)
        {
            return null;
        }

        var (_, deposits, _) = await LinesAsync(db.ClientAccountEntries.Where(e => e.AppointmentId == appointmentId), zone, cancellationToken).ConfigureAwait(false);
        return new VisitDeposit(
            visit.Id,
            visit.ClientId,
            visit.ClientName,
            visit.ServiceName,
            visit.StaffName,
            visit.Start,
            visit.Status,
            visit.Price,
            visit.DepositAmount > 0m ? visit.Price with { Amount = visit.DepositAmount } : null,
            deposits.Count > 0 ? deposits[0] : null);
    }

    public async Task<DepositLine> RecordDepositAsync(Guid appointmentId, decimal? amount, PaymentChoice method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var paid = await RequireMethodAsync(method, cancellationToken).ConfigureAwait(false);
        var tenantId = RequireTenant();
        var visit = await GetDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (visit.Deposit is not null)
        {
            throw new ClientAccountException(ClientAccountRefusal.DepositAlreadyRecorded);
        }

        if (!visit.IsActive)
        {
            throw new ClientAccountException(ClientAccountRefusal.VisitNotActive);
        }

        // What the booking asks for unless the desk typed another amount; a visit that asks for none needs one typed.
        var value = amount ?? visit.Due?.Amount ?? 0m;
        if (!IsAmount(value, visit.Price.Amount))
        {
            throw new ClientAccountException(ClientAccountRefusal.AmountOutOfRange);
        }

        var money = visit.Price with { Amount = value };
        var result = await RecordAsync(payments.RecordDepositAsync(new PaymentRequest(tenantId, appointmentId, money, paid.Method), cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = new ClientAccountEntry
        {
            TenantId = tenantId,
            ClientId = visit.ClientId,
            AppointmentId = appointmentId,
            Kind = ClientAccountEntryKind.DepositPaid,
            Amount = value,
            Currency = money.Currency,
            Method = paid.Method,
            CustomMethodId = paid.CustomMethodId,
            MethodName = paid.MethodName,
            ProviderSystemName = payments.SystemName,
            ProviderReference = result.ProviderReference,
            ByUserId = byUserId,
        };
        await InsertAsync(entry, ClientAccountRefusal.DepositAlreadyRecorded, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Deposit of {Amount} {Currency} for appointment {AppointmentId} received by {Method}, recorded by user {UserId} through {Provider}", value, money.Currency, appointmentId, paid.Method, byUserId, payments.SystemName);
        return await RequireDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DepositLine> ReturnDepositAsync(Guid appointmentId, PaymentChoice method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var paid = await RequireMethodAsync(method, cancellationToken).ConfigureAwait(false);
        var tenantId = RequireTenant();
        var visit = await GetDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        var deposit = visit.Deposit is { IsHeld: true } held ? held : throw new ClientAccountException(ClientAccountRefusal.NoDepositHeld);

        var result = await RecordAsync(payments.RecordDepositReturnAsync(new PaymentRequest(tenantId, appointmentId, deposit.Amount, paid.Method), cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = Related(tenantId, visit.ClientId, appointmentId, ClientAccountEntryKind.DepositReturned, deposit.Amount, deposit.EntryId, byUserId);
        Stamp(entry, paid);
        entry.ProviderSystemName = payments.SystemName;
        entry.ProviderReference = result.ProviderReference;
        await InsertAsync(entry, ClientAccountRefusal.NoDepositHeld, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Deposit {DepositId} of appointment {AppointmentId} returned by {Method}, recorded by user {UserId} through {Provider}", deposit.EntryId, appointmentId, paid.Method, byUserId, payments.SystemName);
        return await RequireDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<VisitBill?> GetBillAsync(Guid appointmentId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
        var visit = await VisitAsync(appointmentId, zone, cancellationToken).ConfigureAwait(false);
        if (visit is null)
        {
            return null;
        }

        var (_, deposits, visitPayments) = await LinesAsync(db.ClientAccountEntries.Where(e => e.AppointmentId == appointmentId), zone, cancellationToken).ConfigureAwait(false);
        var account = await GetAccountAsync(visit.ClientId, cancellationToken).ConfigureAwait(false);

        // Gift vouchers used toward this visit (ADR 0057): no money moved, the visit has that much less to pay.
        var vouchers = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.AppointmentId == appointmentId && e.Kind == ClientAccountEntryKind.VoucherRedeemed)
            .SumAsync(e => e.Amount, cancellationToken).ConfigureAwait(false);

        // The products rung up on this bill, each with the method of the line that paid for it (ADR 0037).
        var productRows = await db.VisitProductLines.AsNoTracking()
            .Where(l => l.AppointmentId == appointmentId)
            .OrderBy(l => l.AddedAt).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var saleIds = productRows.Where(l => l.PaymentEntryId is not null).Select(l => l.PaymentEntryId!.Value).Distinct().ToArray();
        var saleMethods = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => saleIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => new { e.Method, e.CustomMethodId, e.MethodName }, cancellationToken).ConfigureAwait(false);
        var products = productRows
            .Select(l =>
            {
                var paidWith = l.PaymentEntryId is { } saleId ? saleMethods.GetValueOrDefault(saleId) : null;
                return new BillProductLine(l.Id, l.ProductId, l.ProductName, l.Quantity, new Money(l.UnitPrice, l.Currency), paidWith?.Method, l.IsPaid, l.ReturnedQuantity)
                {
                    PaidWithCustomMethodId = paidWith?.CustomMethodId,
                    PaidWithName = paidWith?.MethodName,
                };
            })
            .ToArray();

        return new VisitBill(
            visit.Id,
            visit.ClientId,
            visit.ClientName,
            visit.ServiceName,
            visit.StaffName,
            visit.Start,
            visit.Status,
            visit.Price,
            Held(deposits)?.Amount,
            visitPayments.FirstOrDefault(p => !p.IsPart),
            account.Fees.Where(f => f.Status == FeeStatus.Owed).ToArray(),
            products)
        {
            ServiceId = visit.ServiceId,
            StaffMemberId = visit.StaffMemberId,
            VoucherApplied = vouchers > 0m ? visit.Price with { Amount = vouchers } : null,
            // The parts paid another way before the rest (ADR 0061), oldest first.
            PartPayments = [.. visitPayments.Where(p => p.IsPart).OrderBy(p => p.At).ThenBy(p => p.EntryId)],
        };
    }

    public async Task<VisitBill> PayPartAsync(Guid appointmentId, decimal amount, PaymentChoice method, Guid? byUserId, Guid? partId = null, CancellationToken cancellationToken = default)
    {
        var paid = await RequireMethodAsync(method, cancellationToken).ConfigureAwait(false);
        var tenantId = RequireTenant();

        // Its own transaction when the caller has none, so the lock below holds until the part is written.
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            await LockVisitAsync(appointmentId, cancellationToken).ConfigureAwait(false);
            var bill = await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
            if (partId is { } sent)
            {
                var again = await db.ClientAccountEntries.AsNoTracking().Where(e => e.Id == sent).Select(e => new { e.Kind, e.AppointmentId }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (again is not null)
                {
                    // The same form again (a second click, a reload of the post): one part. Another line's id is not ours.
                    return again.Kind == ClientAccountEntryKind.PartPayment && again.AppointmentId == appointmentId
                        ? bill
                        : throw new ClientAccountException(ClientAccountRefusal.AlreadyBeingRecorded);
                }
            }

            if (!bill.CanPay)
            {
                throw new ClientAccountException(ClientAccountRefusal.VisitNotPayable);
            }

            if (!TillRules.IsPart(amount, bill.Outstanding.Amount))
            {
                // Also a visit already paid, or covered by a deposit or a voucher: nothing is left to take a part of.
                throw new ClientAccountException(ClientAccountRefusal.PartNotLessThanDue);
            }

            var part = bill.Price with { Amount = amount };
            var result = await RecordAsync(payments.RecordInSalonPaymentAsync(new PaymentRequest(tenantId, appointmentId, part, paid.Method), cancellationToken), appointmentId).ConfigureAwait(false);
            var entry = new ClientAccountEntry
            {
                TenantId = tenantId,
                ClientId = bill.ClientId,
                AppointmentId = appointmentId,
                Kind = ClientAccountEntryKind.PartPayment,
                Amount = amount,
                Currency = part.Currency,
                Method = paid.Method,
                CustomMethodId = paid.CustomMethodId,
                MethodName = paid.MethodName,
                ProviderSystemName = payments.SystemName,
                ProviderReference = result.ProviderReference,
                ByUserId = byUserId,
            };
            if (partId is { } chosen)
            {
                // The form's id is the line's id, so the database itself refuses the same part twice.
                entry.Id = chosen;
            }

            await InsertAsync(entry, ClientAccountRefusal.AlreadyBeingRecorded, cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("Visit {AppointmentId} paid in part: {Amount} {Currency} by {Method}, recorded by user {UserId} through {Provider}", appointmentId, amount, part.Currency, paid.Method, byUserId ?? Guid.Empty, payments.SystemName);
            return await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The bill of appointment {appointmentId} was paid in part but cannot be read back.");
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Holds the visit's row until the transaction ends (ADR 0061), so two requests that take money toward the same
    /// visit go one after the other and the second sees what the first wrote. A write that changes nothing: the row
    /// lock is the point.
    /// </summary>
    private Task<int> LockVisitAsync(Guid appointmentId, CancellationToken cancellationToken)
        => db.Appointments.Where(a => a.Id == appointmentId).ExecuteUpdateAsync(s => s.SetProperty(a => a.DurationMinutes, a => a.DurationMinutes), cancellationToken);

    public async Task<VisitBill> AddProductAsync(Guid appointmentId, Guid productId, int quantity, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var tenantId = RequireTenant();
        var bill = await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (!bill.CanPay)
        {
            throw new ClientAccountException(ClientAccountRefusal.VisitNotPayable);
        }

        if (!ProductSaleRules.IsQuantity(quantity))
        {
            throw new ClientAccountException(ClientAccountRefusal.QuantityOutOfRange);
        }

        // Another salon's product is filtered out and reads as unknown; so does one without a price.
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, cancellationToken).ConfigureAwait(false);
        if (product is null || !ProductSaleRules.IsForSale(product))
        {
            throw new ClientAccountException(ClientAccountRefusal.ProductNotForSale);
        }

        // Two tills ringing the same product up at once: the unique index lets one line in, the other try finds it.
        for (var attempt = 1; ; attempt++)
        {
            var line = await db.VisitProductLines
                .FirstOrDefaultAsync(l => l.AppointmentId == appointmentId && l.ProductId == productId && l.PaymentEntryId == null, cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                line = new VisitProductLine
                {
                    TenantId = tenantId,
                    AppointmentId = appointmentId,
                    ProductId = productId,
                    ProductName = product.Name,
                    Currency = product.Currency,
                    AddedAt = clock.GetCurrentInstant(),
                    AddedByUserId = byUserId,
                };
                db.VisitProductLines.Add(line);
            }

            if (!ProductSaleRules.IsQuantity(line.Quantity + quantity))
            {
                db.Entry(line).State = EntityState.Detached;
                throw new ClientAccountException(ClientAccountRefusal.QuantityOutOfRange);
            }

            // One line has one price: the product's price now, also for units rung up a moment ago.
            line.Quantity += quantity;
            line.ProductName = product.Name;
            line.UnitPrice = product.RetailPrice!.Value;
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (DbUpdateException ex) when (attempt == 1 && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.Entry(line).State = EntityState.Detached;
            }
        }

        logger.LogInformation("Product {ProductId} x{Quantity} put on the bill of appointment {AppointmentId} by user {UserId}", productId, quantity, appointmentId, byUserId);
        return await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The bill of appointment {appointmentId} was changed but cannot be read back.");
    }

    public async Task<VisitBill> RemoveProductAsync(Guid appointmentId, Guid lineId, CancellationToken cancellationToken = default)
    {
        var line = await db.VisitProductLines.FirstOrDefaultAsync(l => l.Id == lineId && l.AppointmentId == appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (line.IsPaid)
        {
            throw new ClientAccountException(ClientAccountRefusal.ProductLinePaid);
        }

        // An unpaid line is only what the desk rang up; no money and no stock has moved, so it simply goes.
        db.VisitProductLines.Remove(line);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Product line {LineId} taken off the bill of appointment {AppointmentId}", lineId, appointmentId);
        return await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
    }

    public async Task<VisitBill> ReturnProductAsync(Guid appointmentId, Guid lineId, int quantity, PaymentChoice method, bool backOnShelf, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var paid = await RequireMethodAsync(method, cancellationToken).ConfigureAwait(false);
        var tenantId = RequireTenant();
        // Read without tracking: the units are claimed below in one statement, not by saving this copy.
        var line = await db.VisitProductLines.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lineId && l.AppointmentId == appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (line.PaymentEntryId is not { } saleId)
        {
            throw new ClientAccountException(ClientAccountRefusal.ProductLineNotPaid);
        }

        if (!ProductSaleRules.CanReturn(line.IsPaid, line.Quantity, line.ReturnedQuantity, quantity))
        {
            throw new ClientAccountException(ClientAccountRefusal.ReturnMoreThanKept);
        }

        // The units are claimed in one statement that only succeeds while the client still has them (ADR 0054), so
        // two requests for the same bottle (a double click, two tills) cannot both get the money back.
        var claimed = await db.VisitProductLines
            .Where(l => l.Id == lineId && l.PaymentEntryId != null && l.Quantity - l.ReturnedQuantity >= quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.ReturnedQuantity, l => l.ReturnedQuantity + quantity), cancellationToken).ConfigureAwait(false);
        if (claimed != 1)
        {
            throw new ClientAccountException(ClientAccountRefusal.ReturnMoreThanKept);
        }

        // The money goes back for what the client paid: the price the line was rung up at, not today's price.
        var sale = await db.ClientAccountEntries.AsNoTracking().FirstAsync(e => e.Id == saleId, cancellationToken).ConfigureAwait(false);
        var back = new Money(ProductSaleRules.LineTotal(quantity, line.UnitPrice), line.Currency);
        var result = await RecordAsync(payments.RecordRefundAsync(new PaymentRequest(tenantId, appointmentId, back, paid.Method), cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = Related(tenantId, sale.ClientId, appointmentId, ClientAccountEntryKind.ProductReturn, back, saleId, byUserId);
        Stamp(entry, paid);
        entry.ProviderSystemName = payments.SystemName;
        entry.ProviderReference = result.ProviderReference;
        await InsertAsync(entry, ClientAccountRefusal.ReturnMoreThanKept, cancellationToken).ConfigureAwait(false);

        // A bottle that can be sold again goes back on its shelf; an opened or broken one does not come back at all.
        if (backOnShelf)
        {
            var stocked = await stock.ReturnAsync(line.ProductId, quantity, appointmentId, byUserId, cancellationToken).ConfigureAwait(false);
            if (!stocked.IsRecorded)
            {
                // The caller's transaction takes the money line back with it; nothing is half returned.
                logger.LogWarning("Product {ProductId} returned on appointment {AppointmentId} could not go back on its shelf: {Outcome}", line.ProductId, appointmentId, stocked.Outcome);
                throw new ClientAccountException(ClientAccountRefusal.StockNotRecorded);
            }
        }

        logger.LogInformation("Product line {LineId} of appointment {AppointmentId}: {Quantity} returned, {Amount} {Currency} back by {Method}, on the shelf again: {BackOnShelf}, recorded by user {UserId}", lineId, appointmentId, quantity, back.Amount, back.Currency, paid.Method, backOnShelf, byUserId);
        return await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
    }

    public async Task<VisitBill> PayBillAsync(Guid appointmentId, PaymentChoice method, IReadOnlyCollection<Guid> feeAppointmentIds, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feeAppointmentIds);
        var paid = await RequireMethodAsync(method, cancellationToken).ConfigureAwait(false);
        var tenantId = RequireTenant();

        // A part payment recorded at this very moment must be seen before the rest is worked out (ADR 0061).
        await LockVisitAsync(appointmentId, cancellationToken).ConfigureAwait(false);
        var bill = await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (!bill.CanPay)
        {
            throw new ClientAccountException(ClientAccountRefusal.VisitNotPayable);
        }

        var fees = bill.OpenFees.Where(f => feeAppointmentIds.Contains(f.AppointmentId)).ToArray();
        var due = bill.Outstanding;
        var unpaidProducts = await db.VisitProductLines
            .Where(l => l.AppointmentId == appointmentId && l.PaymentEntryId == null)
            .OrderBy(l => l.AddedAt).ThenBy(l => l.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (due.Amount == 0m && fees.Length == 0 && unpaidProducts.Count == 0)
        {
            throw new ClientAccountException(ClientAccountRefusal.NothingToPay);
        }

        if (due.Amount > 0m)
        {
            // Phase 1: the plugin only records that the salon took the money itself (ADR 0024).
            var result = await RecordAsync(payments.RecordInSalonPaymentAsync(new PaymentRequest(tenantId, appointmentId, due, paid.Method), cancellationToken), appointmentId).ConfigureAwait(false);
            var entry = new ClientAccountEntry
            {
                TenantId = tenantId,
                ClientId = bill.ClientId,
                AppointmentId = appointmentId,
                Kind = ClientAccountEntryKind.VisitPayment,
                Amount = due.Amount,
                Currency = due.Currency,
                Method = paid.Method,
                CustomMethodId = paid.CustomMethodId,
                MethodName = paid.MethodName,
                ProviderSystemName = payments.SystemName,
                ProviderReference = result.ProviderReference,
                ByUserId = byUserId,
            };
            await InsertAsync(entry, ClientAccountRefusal.NothingToPay, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Visit {AppointmentId} paid: {Amount} {Currency} by {Method}, recorded by user {UserId} through {Provider}", appointmentId, due.Amount, due.Currency, paid.Method, byUserId, payments.SystemName);
        }

        if (unpaidProducts.Count > 0)
        {
            // The products on the bill are one more line of money (ADR 0037), kept apart from the visit so the till and
            // the reports can tell services from retail. Every unit leaves its shelf in the same transaction.
            var sold = due with { Amount = unpaidProducts.Sum(l => ProductSaleRules.LineTotal(l.Quantity, l.UnitPrice)) };
            var result = await RecordAsync(payments.RecordInSalonPaymentAsync(new PaymentRequest(tenantId, appointmentId, sold, paid.Method), cancellationToken), appointmentId).ConfigureAwait(false);
            var sale = new ClientAccountEntry
            {
                TenantId = tenantId,
                ClientId = bill.ClientId,
                AppointmentId = appointmentId,
                Kind = ClientAccountEntryKind.ProductSale,
                Amount = sold.Amount,
                Currency = sold.Currency,
                Method = paid.Method,
                CustomMethodId = paid.CustomMethodId,
                MethodName = paid.MethodName,
                ProviderSystemName = payments.SystemName,
                ProviderReference = result.ProviderReference,
                ByUserId = byUserId,
            };
            await InsertAsync(sale, ClientAccountRefusal.NothingToPay, cancellationToken).ConfigureAwait(false);

            // What each product costs the salon today is kept on the line (ADR 0052): the profit of this sale must not
            // move when the cost changes later.
            var soldIds = unpaidProducts.Select(l => l.ProductId).Distinct().ToArray();
            var costs = await db.Products.AsNoTracking()
                .Where(p => soldIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.CostPrice, cancellationToken).ConfigureAwait(false);
            foreach (var line in unpaidProducts)
            {
                line.PaymentEntryId = sale.Id;
                line.UnitCost = costs.GetValueOrDefault(line.ProductId);
                var stocked = await stock.SellAsync(line.ProductId, line.Quantity, appointmentId, byUserId, cancellationToken).ConfigureAwait(false);
                if (!stocked.IsRecorded)
                {
                    // The caller's transaction takes the money line back with it; nothing is half sold.
                    logger.LogWarning("Product {ProductId} on the bill of appointment {AppointmentId} could not leave its shelf: {Outcome}", line.ProductId, appointmentId, stocked.Outcome);
                    throw new ClientAccountException(ClientAccountRefusal.StockNotRecorded);
                }
            }

            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Products on appointment {AppointmentId} paid: {Amount} {Currency} by {Method}, {Lines} lines, recorded by user {UserId} through {Provider}", appointmentId, sold.Amount, sold.Currency, paid.Method, unpaidProducts.Count, byUserId, payments.SystemName);
        }

        // Fees from earlier visits ride on the same bill, each settled by its own line so the account stays readable.
        foreach (var fee in fees)
        {
            await RecordFeePaymentAsync(fee.AppointmentId, method, byUserId, cancellationToken).ConfigureAwait(false);
        }

        return await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The bill of appointment {appointmentId} was paid but cannot be read back.");
    }

    public async Task<ClientAccount> GetAccountAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
        var (fees, deposits, _) = await LinesAsync(db.ClientAccountEntries.Where(e => e.ClientId == clientId), zone, cancellationToken).ConfigureAwait(false);
        if (fees.Count == 0 && deposits.Count == 0)
        {
            return ClientAccount.Empty;
        }

        var owed = fees.Where(f => f.Status == FeeStatus.Owed).Sum(f => f.Amount.Amount);
        return new ClientAccount(owed > 0m ? fees[0].Amount with { Amount = owed } : null, fees, deposits);
    }

    public async Task<IReadOnlyList<PurchaseLine>> GetPurchasesAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);

        // A paid bill line points at the account line that settled it; that line names the client, the time and the method.
        var rows = await (
            from line in db.VisitProductLines.AsNoTracking()
            join sale in db.ClientAccountEntries.AsNoTracking() on line.PaymentEntryId equals sale.Id
            where sale.ClientId == clientId && line.Quantity > line.ReturnedQuantity
            orderby sale.At descending, line.AddedAt descending
            select new { line.AppointmentId, line.ProductName, Quantity = line.Quantity - line.ReturnedQuantity, line.UnitPrice, line.Currency, sale.At, sale.Method, sale.MethodName })
            .Take(PurchaseLine.MaxShown)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return rows
            .Select(r => new PurchaseLine(r.AppointmentId, r.ProductName, r.Quantity, new Money(ProductSaleRules.LineTotal(r.Quantity, r.UnitPrice), r.Currency), r.At.InZone(zone), r.Method) { MethodName = r.MethodName })
            .ToArray();
    }

    /// <summary>Above zero, to the penny, and no more than <paramref name="max"/>.</summary>
    private static bool IsAmount(decimal value, decimal max) => value > 0m && value <= max && decimal.Round(value, 2) == value;

    /// <summary>The posted choice as one of the salon's methods (ADR 0060), or a refusal the desk can read.</summary>
    private async Task<ResolvedPaymentMethod> RequireMethodAsync(PaymentChoice method, CancellationToken cancellationToken)
        => await methods.ResolveAsync(method, cancellationToken).ConfigureAwait(false)
            ?? throw new ClientAccountException(ClientAccountRefusal.MethodNotOffered);

    /// <summary>How the money changed hands, written on the line: the method, and for the salon's own which one and its name.</summary>
    private static void Stamp(ClientAccountEntry entry, ResolvedPaymentMethod paid)
    {
        entry.Method = paid.Method;
        entry.CustomMethodId = paid.CustomMethodId;
        entry.MethodName = paid.MethodName;
    }

    private static DepositLine? Held(IReadOnlyList<DepositLine> deposits) => deposits.FirstOrDefault(d => d.IsHeld);

    /// <summary>A line that closes another one in full: a waiver or payment of a fee, the return of a deposit.</summary>
    private static ClientAccountEntry Related(Guid tenantId, Guid? clientId, Guid appointmentId, ClientAccountEntryKind kind, Money amount, Guid relatedEntryId, Guid? byUserId)
        => new()
        {
            TenantId = tenantId,
            ClientId = clientId,
            AppointmentId = appointmentId,
            Kind = kind,
            Amount = amount.Amount,
            Currency = amount.Currency,
            RelatedEntryId = relatedEntryId,
            ByUserId = byUserId,
        };

    private async Task<PaymentResult> RecordAsync(Task<PaymentResult> call, Guid appointmentId)
    {
        var result = await call.ConfigureAwait(false);
        if (!result.Succeeded)
        {
            logger.LogWarning("Payment plugin {Provider} did not record the money for appointment {AppointmentId}: {Error}", payments.SystemName, appointmentId, result.Error);
            throw new ClientAccountException(ClientAccountRefusal.PaymentNotRecorded);
        }

        return result;
    }

    private async Task InsertAsync(ClientAccountEntry entry, ClientAccountRefusal whenTaken, CancellationToken cancellationToken)
    {
        entry.At = clock.GetCurrentInstant();
        db.ClientAccountEntries.Add(entry);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request got there first (a double click, two tills): the line stays out and the caller is told why.
            db.Entry(entry).State = EntityState.Detached;
            throw new ClientAccountException(whenTaken, ex);
        }
    }

    private async Task<(AppointmentFee Visit, FeeLine Line)> RequireOpenFeeAsync(Guid appointmentId, CancellationToken cancellationToken)
    {
        var visit = await GetFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        var fee = visit.Fee ?? throw new ClientAccountException(ClientAccountRefusal.NoFee);
        return fee.Settlement is null ? (visit, fee) : throw new ClientAccountException(ClientAccountRefusal.AlreadySettled);
    }

    private async Task<FeeLine> RequireFeeAsync(Guid appointmentId, CancellationToken cancellationToken)
        => (await GetFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false))?.Fee
            ?? throw new InvalidOperationException($"The fee of appointment {appointmentId} was written but cannot be read back.");

    private async Task<DepositLine> RequireDepositAsync(Guid appointmentId, CancellationToken cancellationToken)
        => (await GetDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false))?.Deposit
            ?? throw new InvalidOperationException($"The deposit of appointment {appointmentId} was written but cannot be read back.");

    private async Task<Visit?> VisitAsync(Guid appointmentId, DateTimeZone zone, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.Id, a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.Status, a.CancelledAt, a.PriceAmount, a.Currency, a.DepositAmount })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return null;
        }

        var clientName = await db.Clients.AsNoTracking().Where(c => c.Id == appointment.ClientId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == appointment.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        return new Visit(
            appointment.Id,
            appointment.ClientId,
            clientName,
            serviceName,
            staffName,
            appointment.Start.InZone(zone),
            appointment.Status,
            appointment.CancelledAt,
            new Money(appointment.PriceAmount, appointment.Currency),
            appointment.DepositAmount,
            appointment.ServiceId,
            appointment.StaffMemberId);
    }

    /// <summary>
    /// The fees, the deposits and the visit payments among <paramref name="entries"/>, each newest first, a fee with its
    /// settlement and a deposit with its return, all with the names of who acted.
    /// </summary>
    private async Task<(IReadOnlyList<FeeLine> Fees, IReadOnlyList<DepositLine> Deposits, IReadOnlyList<VisitPaymentLine> VisitPayments)> LinesAsync(IQueryable<ClientAccountEntry> entries, DateTimeZone zone, CancellationToken cancellationToken)
    {
        var rows = await entries.AsNoTracking().OrderByDescending(e => e.At).ThenByDescending(e => e.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return ([], [], []);
        }

        var appointmentIds = rows.Where(r => r.AppointmentId is not null).Select(r => r.AppointmentId!.Value).Distinct().ToArray();
        var visits = await db.Appointments.AsNoTracking()
            .Where(a => appointmentIds.Contains(a.Id))
            .Select(a => new { a.Id, a.ServiceId, a.Start })
            .ToDictionaryAsync(a => a.Id, cancellationToken).ConfigureAwait(false);
        var serviceIds = visits.Values.Select(v => v.ServiceId).Distinct().ToArray();
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken).ConfigureAwait(false);
        var userIds = rows.Where(r => r.ByUserId is not null).Select(r => r.ByUserId!.Value).Distinct().ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken).ConfigureAwait(false);
        string? Name(Guid? userId) => userId is { } id && users.TryGetValue(id, out var name) ? name : null;
        (string ServiceName, ZonedDateTime Start) VisitOf(ClientAccountEntry row)
        {
            var visit = row.AppointmentId is { } appointmentId && visits.TryGetValue(appointmentId, out var found) ? found : null;
            var service = visit is not null && services.TryGetValue(visit.ServiceId, out var name) ? name : string.Empty;
            return (service, (visit?.Start ?? row.At).InZone(zone));
        }

        var closing = rows.Where(r => r.RelatedEntryId is not null).ToLookup(r => r.RelatedEntryId!.Value);
        var fees = new List<FeeLine>();
        var deposits = new List<DepositLine>();
        var visitPayments = new List<VisitPaymentLine>();
        foreach (var row in rows)
        {
            if (ClientAccountRules.IsFee(row.Kind))
            {
                var (service, start) = VisitOf(row);
                var settled = closing[row.Id].FirstOrDefault(r => ClientAccountRules.IsSettlement(r.Kind));
                fees.Add(new FeeLine(
                    row.Id,
                    row.AppointmentId ?? Guid.Empty,
                    row.Kind,
                    row.Money,
                    row.At,
                    Name(row.ByUserId),
                    service,
                    start,
                    settled is null ? null : new FeeSettlement(settled.Kind, settled.At, Name(settled.ByUserId), settled.Method, settled.Note, settled.ProviderReference) { MethodName = settled.MethodName }));
            }
            else if (row.Kind == ClientAccountEntryKind.DepositPaid)
            {
                var (service, start) = VisitOf(row);
                var returned = closing[row.Id].FirstOrDefault(r => r.Kind == ClientAccountEntryKind.DepositReturned);
                deposits.Add(new DepositLine(
                    row.Id,
                    row.AppointmentId ?? Guid.Empty,
                    row.Money,
                    row.At,
                    Name(row.ByUserId),
                    row.Method,
                    row.ProviderReference,
                    service,
                    start,
                    returned is null ? null : new DepositReturn(returned.At, Name(returned.ByUserId), returned.Method, returned.ProviderReference) { MethodName = returned.MethodName })
                {
                    CustomMethodId = row.CustomMethodId,
                    MethodName = row.MethodName,
                });
            }
            else if (row.Kind is ClientAccountEntryKind.VisitPayment or ClientAccountEntryKind.PartPayment)
            {
                visitPayments.Add(new VisitPaymentLine(row.Id, row.AppointmentId ?? Guid.Empty, row.Money, row.At, Name(row.ByUserId), row.Method, row.ProviderReference)
                {
                    MethodName = row.MethodName,
                    IsPart = row.Kind == ClientAccountEntryKind.PartPayment,
                });
            }
        }

        return (fees, deposits, visitPayments);
    }

    private Guid RequireTenant()
        => tenantContext.TenantId ?? throw new TenantViolationException("A client account belongs to a salon, but no tenant is set in the current scope.");

    private async Task<DateTimeZone> ZoneAsync(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
    }

    /// <summary>One appointment as the account needs it: who, what, when (salon zone), how it stands and what was asked for.</summary>
    private sealed record Visit(
        Guid Id,
        Guid ClientId,
        string ClientName,
        string ServiceName,
        string StaffName,
        ZonedDateTime Start,
        AppointmentStatus Status,
        Instant? CancelledAt,
        Money Price,
        decimal DepositAmount,
        Guid ServiceId,
        Guid StaffMemberId);
}