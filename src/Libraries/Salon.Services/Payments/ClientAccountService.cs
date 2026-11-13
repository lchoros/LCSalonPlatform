using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Npgsql;

using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Services.Tenancy;

namespace Salon.Services.Payments;

/// <summary>
/// Reads and writes through the tenant filter. Lines are only ever inserted; two partial unique indexes (one fee per
/// visit, one settlement per fee) turn a double submit into a refusal instead of a second line (ADR 0022).
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
        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == appointmentId)
            .Select(a => new { a.Id, a.ClientId, a.ServiceId, a.StaffMemberId, a.Start, a.Status, a.CancelledAt, a.PriceAmount, a.Currency })
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (appointment is null)
        {
            return null;
        }

        var clientName = await db.Clients.AsNoTracking().Where(c => c.Id == appointment.ClientId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var serviceName = await db.Services.AsNoTracking().Where(s => s.Id == appointment.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;
        var staffName = await db.StaffMembers.AsNoTracking().Where(s => s.Id == appointment.StaffMemberId).Select(s => s.DisplayName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? string.Empty;

        var price = new Core.Domain.Money(appointment.PriceAmount, appointment.Currency);
        var fees = await LinesAsync(db.ClientAccountEntries.Where(e => e.AppointmentId == appointmentId), zone, cancellationToken).ConfigureAwait(false);
        var fee = fees.Count > 0 ? fees[0] : null;
        FeeProposal? proposal = null;
        if (fee is null)
        {
            var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken).ConfigureAwait(false);
            proposal = CancellationPolicy.Propose(appointment.Status, appointment.Start, appointment.CancelledAt, price, policy);
        }

        return new AppointmentFee(appointment.Id, appointment.ClientId, clientName, serviceName, staffName, appointment.Start.InZone(zone), appointment.Status, price, proposal, fee);
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
        var value = amount ?? proposal.Amount.Amount;
        if (value <= 0m || value > visit.Price.Amount || decimal.Round(value, 2) != value)
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
            At = clock.GetCurrentInstant(),
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

        var entry = Settlement(tenantId, fee.Visit, fee.Line, ClientAccountEntryKind.FeeWaived, byUserId);
        entry.Note = why.Length <= ClientAccountEntry.NoteMaxLength ? why : why[..ClientAccountEntry.NoteMaxLength];
        await InsertAsync(entry, ClientAccountRefusal.AlreadySettled, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Fee {FeeId} of appointment {AppointmentId} waived by user {UserId}", fee.Line.EntryId, appointmentId, byUserId);
        return await RequireFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FeeLine> RecordFeePaymentAsync(Guid appointmentId, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(method))
        {
            throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown payment method.");
        }

        var tenantId = RequireTenant();
        var fee = await RequireOpenFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);

        // Phase 1: the plugin only records that the salon took the money itself. An online gateway (M3) must claim the
        // settlement before it charges; see ADR 0022.
        var result = await payments.RecordNoShowFeeAsync(new PaymentRequest(tenantId, appointmentId, fee.Line.Amount, method, fee.Line.Kind.ToString()), cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            logger.LogWarning("Payment plugin {Provider} did not record the fee payment for appointment {AppointmentId}: {Error}", payments.SystemName, appointmentId, result.Error);
            throw new ClientAccountException(ClientAccountRefusal.PaymentNotRecorded);
        }

        var entry = Settlement(tenantId, fee.Visit, fee.Line, ClientAccountEntryKind.FeePayment, byUserId);
        entry.Method = method;
        entry.ProviderSystemName = payments.SystemName;
        entry.ProviderReference = result.ProviderReference;
        await InsertAsync(entry, ClientAccountRefusal.AlreadySettled, cancellationToken).ConfigureAwait(false);

        logger.LogInformation("Fee {FeeId} of appointment {AppointmentId} paid by {Method}, recorded by user {UserId} through {Provider}", fee.Line.EntryId, appointmentId, method, byUserId, payments.SystemName);
        return await RequireFeeAsync(appointmentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ClientAccount> GetAccountAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
        var fees = await LinesAsync(db.ClientAccountEntries.Where(e => e.ClientId == clientId), zone, cancellationToken).ConfigureAwait(false);
        if (fees.Count == 0)
        {
            return ClientAccount.Empty;
        }

        var owed = fees.Where(f => f.Status == FeeStatus.Owed).Sum(f => f.Amount.Amount);
        return new ClientAccount(owed > 0m ? fees[0].Amount with { Amount = owed } : null, fees);
    }

    private static ClientAccountEntry Settlement(Guid tenantId, AppointmentFee visit, FeeLine fee, ClientAccountEntryKind kind, Guid? byUserId)
        => new()
        {
            TenantId = tenantId,
            ClientId = visit.ClientId,
            AppointmentId = visit.AppointmentId,
            Kind = kind,
            Amount = fee.Amount.Amount,
            Currency = fee.Amount.Currency,
            RelatedEntryId = fee.EntryId,
            ByUserId = byUserId,
        };

    private async Task InsertAsync(ClientAccountEntry entry, ClientAccountRefusal whenTaken, CancellationToken cancellationToken)
    {
        if (entry.At == default)
        {
            entry.At = clock.GetCurrentInstant();
        }

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

    /// <summary>The fees among <paramref name="entries"/>, newest first, each with its settlement, visit and the names of who acted.</summary>
    private async Task<IReadOnlyList<FeeLine>> LinesAsync(IQueryable<ClientAccountEntry> entries, DateTimeZone zone, CancellationToken cancellationToken)
    {
        var rows = await entries.AsNoTracking().OrderByDescending(e => e.At).ThenByDescending(e => e.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return [];
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

        var settlements = rows.Where(r => ClientAccountRules.IsSettlement(r.Kind) && r.RelatedEntryId is not null).ToLookup(r => r.RelatedEntryId!.Value);
        var lines = new List<FeeLine>();
        foreach (var row in rows.Where(r => ClientAccountRules.IsFee(r.Kind)))
        {
            var visit = row.AppointmentId is { } appointmentId && visits.TryGetValue(appointmentId, out var found) ? found : null;
            var settled = settlements[row.Id].FirstOrDefault();
            lines.Add(new FeeLine(
                row.Id,
                row.AppointmentId ?? Guid.Empty,
                row.Kind,
                row.Money,
                row.At,
                Name(row.ByUserId),
                visit is not null && services.TryGetValue(visit.ServiceId, out var service) ? service : string.Empty,
                (visit?.Start ?? row.At).InZone(zone),
                settled is null ? null : new FeeSettlement(settled.Kind, settled.At, Name(settled.ByUserId), settled.Method, settled.Note, settled.ProviderReference)));
        }

        return lines;
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
}