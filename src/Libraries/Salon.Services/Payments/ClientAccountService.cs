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

    public async Task<FeeLine> RecordFeePaymentAsync(Guid appointmentId, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        RequireKnown(method);
        var tenantId = RequireTenant();
        var fee = await RequireOpenFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);

        // Phase 1: the plugin only records that the salon took the money itself. An online gateway (M3) must claim the
        // settlement before it charges; see ADR 0022.
        var request = new PaymentRequest(tenantId, appointmentId, fee.Line.Amount, method, fee.Line.Kind.ToString());
        var result = await RecordAsync(payments.RecordNoShowFeeAsync(request, cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = Related(tenantId, fee.Visit.ClientId, appointmentId, ClientAccountEntryKind.FeePayment, fee.Line.Amount, fee.Line.EntryId, byUserId);
        entry.Method = method;
        entry.ProviderSystemName = payments.SystemName;
        entry.ProviderReference = result.ProviderReference;
        await InsertAsync(entry, ClientAccountRefusal.AlreadySettled, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Fee {FeeId} of appointment {AppointmentId} paid by {Method}, recorded by user {UserId} through {Provider}", fee.Line.EntryId, appointmentId, method, byUserId, payments.SystemName);
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

    public async Task<DepositLine> RecordDepositAsync(Guid appointmentId, decimal? amount, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        RequireKnown(method);
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
        var result = await RecordAsync(payments.RecordDepositAsync(new PaymentRequest(tenantId, appointmentId, money, method), cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = new ClientAccountEntry
        {
            TenantId = tenantId,
            ClientId = visit.ClientId,
            AppointmentId = appointmentId,
            Kind = ClientAccountEntryKind.DepositPaid,
            Amount = value,
            Currency = money.Currency,
            Method = method,
            ProviderSystemName = payments.SystemName,
            ProviderReference = result.ProviderReference,
            ByUserId = byUserId,
        };
        await InsertAsync(entry, ClientAccountRefusal.DepositAlreadyRecorded, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Deposit of {Amount} {Currency} for appointment {AppointmentId} received by {Method}, recorded by user {UserId} through {Provider}", value, money.Currency, appointmentId, method, byUserId, payments.SystemName);
        return await RequireDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DepositLine> ReturnDepositAsync(Guid appointmentId, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        RequireKnown(method);
        var tenantId = RequireTenant();
        var visit = await GetDepositAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        var deposit = visit.Deposit is { IsHeld: true } held ? held : throw new ClientAccountException(ClientAccountRefusal.NoDepositHeld);

        var result = await RecordAsync(payments.RecordDepositReturnAsync(new PaymentRequest(tenantId, appointmentId, deposit.Amount, method), cancellationToken), appointmentId).ConfigureAwait(false);

        var entry = Related(tenantId, visit.ClientId, appointmentId, ClientAccountEntryKind.DepositReturned, deposit.Amount, deposit.EntryId, byUserId);
        entry.Method = method;
        entry.ProviderSystemName = payments.SystemName;
        entry.ProviderReference = result.ProviderReference;
        await InsertAsync(entry, ClientAccountRefusal.NoDepositHeld, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Deposit {DepositId} of appointment {AppointmentId} returned by {Method}, recorded by user {UserId} through {Provider}", deposit.EntryId, appointmentId, method, byUserId, payments.SystemName);
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
            visitPayments.Count > 0 ? visitPayments[0] : null,
            account.Fees.Where(f => f.Status == FeeStatus.Owed).ToArray());
    }

    public async Task<VisitBill> PayBillAsync(Guid appointmentId, PaymentMethod method, IReadOnlyCollection<Guid> feeAppointmentIds, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(feeAppointmentIds);
        RequireKnown(method);
        var tenantId = RequireTenant();
        var bill = await GetBillAsync(appointmentId, cancellationToken).ConfigureAwait(false) ?? throw new ClientAccountException(ClientAccountRefusal.NotFound);
        if (!bill.CanPay)
        {
            throw new ClientAccountException(ClientAccountRefusal.VisitNotPayable);
        }

        var fees = bill.OpenFees.Where(f => feeAppointmentIds.Contains(f.AppointmentId)).ToArray();
        var due = bill.Outstanding;
        if (due.Amount == 0m && fees.Length == 0)
        {
            throw new ClientAccountException(ClientAccountRefusal.NothingToPay);
        }

        if (due.Amount > 0m)
        {
            // Phase 1: the plugin only records that the salon took the money itself (ADR 0024).
            var result = await RecordAsync(payments.RecordInSalonPaymentAsync(new PaymentRequest(tenantId, appointmentId, due, method), cancellationToken), appointmentId).ConfigureAwait(false);
            var entry = new ClientAccountEntry
            {
                TenantId = tenantId,
                ClientId = bill.ClientId,
                AppointmentId = appointmentId,
                Kind = ClientAccountEntryKind.VisitPayment,
                Amount = due.Amount,
                Currency = due.Currency,
                Method = method,
                ProviderSystemName = payments.SystemName,
                ProviderReference = result.ProviderReference,
                ByUserId = byUserId,
            };
            await InsertAsync(entry, ClientAccountRefusal.NothingToPay, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Visit {AppointmentId} paid: {Amount} {Currency} by {Method}, recorded by user {UserId} through {Provider}", appointmentId, due.Amount, due.Currency, method, byUserId, payments.SystemName);
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

    /// <summary>Above zero, to the penny, and no more than <paramref name="max"/>.</summary>
    private static bool IsAmount(decimal value, decimal max) => value > 0m && value <= max && decimal.Round(value, 2) == value;

    private static void RequireKnown(PaymentMethod method)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown payment method.");
        }
    }

    private static DepositLine? Held(IReadOnlyList<DepositLine> deposits) => deposits.FirstOrDefault(d => d.IsHeld);

    /// <summary>A line that closes another one in full: a waiver or payment of a fee, the return of a deposit.</summary>
    private static ClientAccountEntry Related(Guid tenantId, Guid clientId, Guid appointmentId, ClientAccountEntryKind kind, Money amount, Guid relatedEntryId, Guid? byUserId)
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
            appointment.DepositAmount);
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
                    settled is null ? null : new FeeSettlement(settled.Kind, settled.At, Name(settled.ByUserId), settled.Method, settled.Note, settled.ProviderReference)));
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
                    returned is null ? null : new DepositReturn(returned.At, Name(returned.ByUserId), returned.Method, returned.ProviderReference)));
            }
            else if (row.Kind == ClientAccountEntryKind.VisitPayment)
            {
                visitPayments.Add(new VisitPaymentLine(row.Id, row.AppointmentId ?? Guid.Empty, row.Money, row.At, Name(row.ByUserId), row.Method, row.ProviderReference));
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
        decimal DepositAmount);
}