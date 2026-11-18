using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Npgsql;
using Salon.Core.Domain;
using Salon.Core.Paging;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Payments;

/// <summary>A gift voucher as a list or a look-up shows it.</summary>
/// <param name="Code">Formatted for reading aloud: two groups of five.</param>
/// <param name="SoldOn">In the salon's zone.</param>
public sealed record VoucherSummary(Guid Id, string Code, Money Initial, Money Left, GiftVoucherStatus Status, LocalDate SoldOn, LocalDate? ExpiresOn, string? Note)
{
    public bool CanBeUsed => Status == GiftVoucherStatus.Active;
}

/// <summary>One use of a voucher toward a visit.</summary>
/// <param name="At">In the salon's zone.</param>
public sealed record VoucherUse(ZonedDateTime At, Guid AppointmentId, string ClientName, Money Amount);

/// <summary>A voucher with its story: how it was sold, every use, and whether the money can still be given back.</summary>
/// <param name="SoldWith">How the buyer paid.</param>
/// <param name="SoldByName">Who sold it; null when the login is gone or unknown.</param>
/// <param name="RefundedOn">The day the salon gave the money back; null for a voucher that stands.</param>
public sealed record VoucherCard(VoucherSummary Voucher, PaymentMethod SoldWith, string? SoldByName, IReadOnlyList<VoucherUse> Uses, bool CanRefund, LocalDate? RefundedOn)
{
    /// <summary>Which of the salon's own methods the buyer paid by, with its name on that day (ADR 0060); null for a built-in one.</summary>
    public Guid? SoldWithCustomMethodId { get; init; }

    public string? SoldWithName { get; init; }

    /// <summary>How the buyer paid, as a form offers it: the money goes back the same way unless the desk says otherwise.</summary>
    public PaymentChoice PaidWith => new(SoldWith, SoldWithCustomMethodId);
}

/// <param name="Outstanding">What the salon owes on vouchers that can still be used: the sum of what is left on them.</param>
/// <param name="ActiveCount">How many vouchers that is.</param>
public sealed record VoucherList(Paged<VoucherSummary> Vouchers, bool IncludesSpent, string? Search, Money Outstanding, int ActiveCount)
{
    public const int PageSize = 20;
}

/// <param name="Amount">What went onto the bill.</param>
/// <param name="Left">What is still on the voucher.</param>
public sealed record VoucherApplied(string Code, Money Amount, Money Left, VisitBill Bill);

public enum GiftVoucherRefusal
{
    /// <summary>The salon has no voucher with this code, or this id.</summary>
    NotFound,

    /// <summary>Not an amount of money a voucher can be sold for.</summary>
    AmountInvalid,

    /// <summary>A voucher cannot be bought with a voucher.</summary>
    MethodNotAllowed,

    /// <summary>The last day is already past.</summary>
    ExpiryPast,

    NoteTooLong,

    /// <summary>The payment plugin did not record the money.</summary>
    PaymentNotRecorded,

    /// <summary>The same form is being recorded by another request at this moment.</summary>
    AlreadyBeingRecorded,

    /// <summary>The voucher's last day has passed.</summary>
    Expired,

    /// <summary>Nothing is left on the voucher.</summary>
    NothingLeft,

    /// <summary>The salon gave the money back for this voucher.</summary>
    Refunded,

    /// <summary>The visit is unknown, or the client has not turned up for it yet.</summary>
    VisitNotPayable,

    /// <summary>The visit has nothing left to pay.</summary>
    NothingToPay,

    /// <summary>This voucher is on this visit's bill already.</summary>
    AlreadyOnThisBill,

    /// <summary>The voucher was used, or the money was given back already.</summary>
    NotRefundable,
}

public sealed class GiftVoucherException : Exception
{
    public GiftVoucherException()
        : this(GiftVoucherRefusal.NotFound)
    {
    }

    public GiftVoucherException(string message)
        : base(message)
    {
    }

    public GiftVoucherException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public GiftVoucherException(GiftVoucherRefusal refusal)
        : base($"The gift voucher request was refused: {refusal}.")
    {
        Refusal = refusal;
    }

    public GiftVoucherRefusal Refusal { get; }
}

/// <summary>
/// Gift vouchers (ADR 0057): sold at the desk for money today, used later toward a visit. Selling is a line of money
/// with no client; using one puts a line on the client's account that moves no money and leaves the visit that much
/// less to pay; what is left on a voucher is what the salon still owes.
/// </summary>
public interface IGiftVoucherService
{
    /// <summary>
    /// Sells a voucher. <paramref name="saleId"/> is the form's own id for the sale: the same form sent twice is one
    /// voucher. Throws <see cref="GiftVoucherException"/> with the reason when nothing was recorded.
    /// </summary>
    Task<VoucherSummary> SellAsync(decimal amount, PaymentChoice method, LocalDate? expiresOn, string? note, Guid? byUserId, Guid? saleId = null, CancellationToken cancellationToken = default);

    /// <summary>The day a voucher sold today would expire by default.</summary>
    Task<LocalDate?> DefaultExpiryAsync(CancellationToken cancellationToken = default);

    Task<VoucherSummary?> FindByCodeAsync(string? code, CancellationToken cancellationToken = default);

    Task<VoucherCard?> GetAsync(Guid voucherId, CancellationToken cancellationToken = default);

    /// <summary>The vouchers, newest first: only those that can still be used, or every one. <paramref name="search"/> is a code or its beginning.</summary>
    Task<VoucherList> ListAsync(bool includeSpent, string? search, int? page, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uses a voucher toward a visit: what is left to pay for the visit, or all the voucher has if that is less. No
    /// money changes hands. Throws <see cref="GiftVoucherException"/> with the reason when nothing was recorded.
    /// </summary>
    Task<VoucherApplied> ApplyAsync(Guid appointmentId, string? code, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>Gives the money back for a voucher nobody has used. Throws <see cref="GiftVoucherException"/> with the reason otherwise.</summary>
    Task<Money> RefundAsync(Guid voucherId, PaymentChoice method, Guid? byUserId, CancellationToken cancellationToken = default);
}

internal sealed class GiftVoucherService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClientAccountService account,
    IPaymentProvider payments,
    IPaymentMethodService methods,
    IClock clock,
    ILogger<GiftVoucherService> logger) : IGiftVoucherService
{
    public async Task<VoucherSummary> SellAsync(decimal amount, PaymentChoice method, LocalDate? expiresOn, string? note, Guid? byUserId, Guid? saleId = null, CancellationToken cancellationToken = default)
    {
        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        var today = now.InZone(zone).Date;
        if (saleId is { } sent)
        {
            var again = await db.GiftVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.SaleEntryId == sent, cancellationToken).ConfigureAwait(false);
            if (again is not null)
            {
                // The same form again (a second click, a reload of the post): one voucher.
                return Summary(again, zone, today);
            }
        }

        if (!GiftVoucherRules.IsAmount(amount))
        {
            throw new GiftVoucherException(GiftVoucherRefusal.AmountInvalid);
        }

        // Any method the salon takes but another voucher (ADR 0060).
        var paid = (GiftVoucherRules.CanBePaidWith(method) ? await methods.ResolveAsync(method, cancellationToken).ConfigureAwait(false) : null)
            ?? throw new GiftVoucherException(GiftVoucherRefusal.MethodNotAllowed);

        if (expiresOn is { } last && last < today)
        {
            throw new GiftVoucherException(GiftVoucherRefusal.ExpiryPast);
        }

        if (!GiftVoucherRules.NoteFits(note))
        {
            throw new GiftVoucherException(GiftVoucherRefusal.NoteTooLong);
        }

        var money = new Money(amount, tenant.Currency);
        var result = await payments.RecordInSalonPaymentAsync(new PaymentRequest(tenant.Id, null, money, paid.Method), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            logger.LogWarning("Payment plugin {Provider} did not record the money for a gift voucher: {Error}", payments.SystemName, result.Error);
            throw new GiftVoucherException(GiftVoucherRefusal.PaymentNotRecorded);
        }

        var sale = new ClientAccountEntry
        {
            TenantId = tenant.Id,
            ClientId = null,
            AppointmentId = null,
            Kind = ClientAccountEntryKind.VoucherSale,
            Amount = amount,
            Currency = tenant.Currency,
            Method = paid.Method,
            CustomMethodId = paid.CustomMethodId,
            MethodName = paid.MethodName,
            ProviderSystemName = payments.SystemName,
            ProviderReference = result.ProviderReference,
            ByUserId = byUserId,
            At = now,
        };
        if (saleId is { } chosen)
        {
            // The form's id is the line's id, so the database itself refuses the same sale twice.
            sale.Id = chosen;
        }

        var voucher = new GiftVoucher
        {
            TenantId = tenant.Id,
            Code = await FreshCodeAsync(cancellationToken).ConfigureAwait(false),
            InitialAmount = amount,
            Balance = amount,
            Currency = tenant.Currency,
            SoldAt = now,
            SoldByUserId = byUserId,
            SaleEntryId = sale.Id,
            ExpiresOn = expiresOn,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
        db.ClientAccountEntries.Add(sale);
        db.GiftVouchers.Add(voucher);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request with the same form got there first; the caller's transaction takes this one back.
            throw new GiftVoucherException(GiftVoucherRefusal.AlreadyBeingRecorded);
        }

        // The user id is written as a plain id (all zeros for nobody): it travels here beside what the form posted,
        // and a log line takes only values whose type cannot carry text (ADR 0060 amendment).
        logger.LogInformation("Gift voucher {VoucherId} sold for {Amount} {Currency} by {Method}, recorded by user {UserId} through {Provider}", voucher.Id, amount, tenant.Currency, paid.Method, byUserId ?? Guid.Empty, payments.SystemName);
        return Summary(voucher, zone, today);
    }

    public async Task<LocalDate?> DefaultExpiryAsync(CancellationToken cancellationToken = default)
    {
        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        return GiftVoucherRules.ExpiresOn(clock.GetCurrentInstant().InZone(zone).Date, GiftVoucherRules.DefaultValidMonths);
    }

    public async Task<VoucherSummary?> FindByCodeAsync(string? code, CancellationToken cancellationToken = default)
    {
        if (GiftVoucherRules.Normalize(code) is not { } normalized)
        {
            return null;
        }

        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var voucher = await db.GiftVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Code == normalized, cancellationToken).ConfigureAwait(false);
        return voucher is null ? null : Summary(voucher, zone, clock.GetCurrentInstant().InZone(zone).Date);
    }

    public async Task<VoucherCard?> GetAsync(Guid voucherId, CancellationToken cancellationToken = default)
    {
        var voucher = await db.GiftVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken).ConfigureAwait(false);
        if (voucher is null)
        {
            return null;
        }

        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        var sale = await db.ClientAccountEntries.AsNoTracking().FirstAsync(e => e.Id == voucher.SaleEntryId, cancellationToken).ConfigureAwait(false);

        // A sale recorded under the wrong method and put right reads as it was paid (ADR 0064).
        var soldWith = (await MethodCorrections.LatestAsync(db, [sale.Id], cancellationToken).ConfigureAwait(false)).Of(sale.Id, sale.Method, sale.CustomMethodId, sale.MethodName);
        var soldBy = voucher.SoldByUserId is { } userId
            ? await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var uses = await (
            from e in db.ClientAccountEntries.AsNoTracking()
            join c in db.Clients.AsNoTracking() on e.ClientId equals c.Id
            where e.Kind == ClientAccountEntryKind.VoucherRedeemed && e.RelatedEntryId == voucher.SaleEntryId && e.AppointmentId != null
            orderby e.At, e.Id
            select new { e.At, AppointmentId = e.AppointmentId!.Value, c.FirstName, c.LastName, e.Amount, e.Currency })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return new VoucherCard(
            Summary(voucher, zone, today),
            soldWith?.Method ?? PaymentMethod.Cash,
            soldBy,
            [.. uses.Select(u => new VoucherUse(u.At.InZone(zone), u.AppointmentId, u.FirstName + " " + u.LastName, new Money(u.Amount, u.Currency)))],
            GiftVoucherRules.CanRefund(voucher.InitialAmount, voucher.Balance, voucher.RefundedAt),
            voucher.RefundedAt?.InZone(zone).Date)
        {
            SoldWithCustomMethodId = soldWith?.CustomMethodId,
            SoldWithName = soldWith?.MethodName,
        };
    }

    public async Task<VoucherList> ListAsync(bool includeSpent, string? search, int? page, CancellationToken cancellationToken = default)
    {
        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var today = clock.GetCurrentInstant().InZone(zone).Date;

        // A salon sells tens or a few hundred vouchers a year: read them once and work in memory with the rules.
        var all = (await db.GiftVouchers.AsNoTracking().OrderByDescending(v => v.SoldAt).ThenByDescending(v => v.Id).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(v => Summary(v, zone, today))
            .ToList();
        var active = all.Where(v => v.CanBeUsed).ToList();
        var typed = string.IsNullOrWhiteSpace(search) ? null : new string([.. search.Where(c => c is not (' ' or '-')).Select(char.ToUpperInvariant)]);
        var shown = (includeSpent || typed is not null ? all : active)
            .Where(v => typed is null || v.Code.Replace("-", string.Empty, StringComparison.Ordinal).StartsWith(typed, StringComparison.Ordinal))
            .ToList();

        return new VoucherList(
            Pages.Cut(shown, page, VoucherList.PageSize),
            includeSpent,
            typed,
            new Money(active.Sum(v => v.Left.Amount), tenant.Currency),
            active.Count);
    }

    public async Task<VoucherApplied> ApplyAsync(Guid appointmentId, string? code, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var (tenant, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        var normalized = GiftVoucherRules.Normalize(code) ?? throw new GiftVoucherException(GiftVoucherRefusal.NotFound);
        var voucher = await db.GiftVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Code == normalized, cancellationToken).ConfigureAwait(false)
            ?? throw new GiftVoucherException(GiftVoucherRefusal.NotFound);
        switch (GiftVoucherRules.Status(voucher.Balance, voucher.ExpiresOn, voucher.RefundedAt, now.InZone(zone).Date))
        {
            case GiftVoucherStatus.Refunded:
                throw new GiftVoucherException(GiftVoucherRefusal.Refunded);
            case GiftVoucherStatus.Used:
                throw new GiftVoucherException(GiftVoucherRefusal.NothingLeft);
            case GiftVoucherStatus.Expired:
                throw new GiftVoucherException(GiftVoucherRefusal.Expired);
        }

        var bill = await account.GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false);
        if (bill is null || !bill.CanPay)
        {
            throw new GiftVoucherException(GiftVoucherRefusal.VisitNotPayable);
        }

        var amount = GiftVoucherRules.Applies(voucher.Balance, bill.Outstanding.Amount);
        if (amount <= 0m)
        {
            throw new GiftVoucherException(GiftVoucherRefusal.NothingToPay);
        }

        // The claim on the voucher and the line on the bill stand or fall together, also when nobody opened a
        // transaction around this call.
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            // One statement that only succeeds while the voucher still has the money (as for returns, ADR 0054).
            var claimed = await db.GiftVouchers
                .Where(v => v.Id == voucher.Id && v.RefundedAt == null && v.Balance >= amount)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Balance, v => v.Balance - amount), cancellationToken).ConfigureAwait(false);
            if (claimed != 1)
            {
                throw new GiftVoucherException(GiftVoucherRefusal.NothingLeft);
            }

            db.ClientAccountEntries.Add(new ClientAccountEntry
            {
                TenantId = tenant.Id,
                ClientId = bill.ClientId,
                AppointmentId = appointmentId,
                Kind = ClientAccountEntryKind.VoucherRedeemed,
                Amount = amount,
                Currency = voucher.Currency,
                // No method: no money changed hands today (ADR 0024 counts a line with a method as money that moved).
                Method = null,
                RelatedEntryId = voucher.SaleEntryId,
                ByUserId = byUserId,
                At = now,
            });
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The same voucher on the same visit a second time (a double click, two tills): the claim goes back.
                throw new GiftVoucherException(GiftVoucherRefusal.AlreadyOnThisBill);
            }

            if (own is not null)
            {
                await own.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch
        {
            if (own is not null)
            {
                await own.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }

        logger.LogInformation("Gift voucher {VoucherId}: {Amount} {Currency} used toward appointment {AppointmentId}, recorded by user {UserId}", voucher.Id, amount, voucher.Currency, appointmentId, byUserId);
        var after = await account.GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"The bill of appointment {appointmentId} was changed but cannot be read back.");
        return new VoucherApplied(GiftVoucherRules.Format(voucher.Code), new Money(amount, voucher.Currency), new Money(voucher.Balance - amount, voucher.Currency), after);
    }

    public async Task<Money> RefundAsync(Guid voucherId, PaymentChoice method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var (tenant, _) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        var voucher = await db.GiftVouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == voucherId, cancellationToken).ConfigureAwait(false)
            ?? throw new GiftVoucherException(GiftVoucherRefusal.NotFound);
        // Any method the salon takes but another voucher (ADR 0060).
        var paid = (GiftVoucherRules.CanBePaidWith(method) ? await methods.ResolveAsync(method, cancellationToken).ConfigureAwait(false) : null)
            ?? throw new GiftVoucherException(GiftVoucherRefusal.MethodNotAllowed);

        if (!GiftVoucherRules.CanRefund(voucher.InitialAmount, voucher.Balance, voucher.RefundedAt))
        {
            throw new GiftVoucherException(GiftVoucherRefusal.NotRefundable);
        }

        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            // Claimed in one statement: only an untouched voucher, and only once.
            var claimed = await db.GiftVouchers
                .Where(v => v.Id == voucherId && v.RefundedAt == null && v.Balance == v.InitialAmount)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.Balance, 0m).SetProperty(v => v.RefundedAt, now), cancellationToken).ConfigureAwait(false);
            if (claimed != 1)
            {
                throw new GiftVoucherException(GiftVoucherRefusal.NotRefundable);
            }

            var back = voucher.Initial;
            var result = await payments.RecordRefundAsync(new PaymentRequest(tenant.Id, null, back, paid.Method), cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                logger.LogWarning("Payment plugin {Provider} did not record the money given back for gift voucher {VoucherId}: {Error}", payments.SystemName, voucherId, result.Error);
                throw new GiftVoucherException(GiftVoucherRefusal.PaymentNotRecorded);
            }

            db.ClientAccountEntries.Add(new ClientAccountEntry
            {
                TenantId = tenant.Id,
                ClientId = null,
                AppointmentId = null,
                Kind = ClientAccountEntryKind.VoucherRefund,
                Amount = back.Amount,
                Currency = back.Currency,
                Method = paid.Method,
                CustomMethodId = paid.CustomMethodId,
                MethodName = paid.MethodName,
                ProviderSystemName = payments.SystemName,
                ProviderReference = result.ProviderReference,
                RelatedEntryId = voucher.SaleEntryId,
                ByUserId = byUserId,
                At = now,
            });
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("Gift voucher {VoucherId}: {Amount} {Currency} given back by {Method}, recorded by user {UserId}", voucherId, back.Amount, back.Currency, paid.Method, byUserId);
            return back;
        }
        catch
        {
            if (own is not null)
            {
                await own.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>A code no voucher of this salon has; a clash is as good as impossible, and is still checked.</summary>
    private async Task<string> FreshCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = GiftVoucherRules.NewCode();
            if (!await db.GiftVouchers.AnyAsync(v => v.Code == code, cancellationToken).ConfigureAwait(false))
            {
                return code;
            }
        }

        throw new InvalidOperationException("No free gift voucher code was found in five draws.");
    }

    private static VoucherSummary Summary(GiftVoucher voucher, DateTimeZone zone, LocalDate today)
        => new(
            voucher.Id,
            GiftVoucherRules.Format(voucher.Code),
            voucher.Initial,
            voucher.Left,
            GiftVoucherRules.Status(voucher.Balance, voucher.ExpiresOn, voucher.RefundedAt, today),
            voucher.SoldAt.InZone(zone).Date,
            voucher.ExpiresOn,
            voucher.Note);

    private async Task<(TenantInfo Tenant, DateTimeZone Zone)> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Gift vouchers belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenant, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc);
    }
}