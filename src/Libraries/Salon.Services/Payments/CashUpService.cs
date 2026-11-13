using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using NodaTime;

using Npgsql;

using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Payments;

/// <summary>
/// Reads the day's lines through the tenant filter and adds them up with <see cref="TillRules"/>. The unique row per
/// salon and day is what makes a close happen once (ADR 0024).
/// </summary>
internal sealed class CashUpService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClock clock,
    ILogger<CashUpService> logger) : ICashUpService
{
    public async Task<CashDay> GetDayAsync(LocalDate? day, CancellationToken cancellationToken = default)
    {
        var (tenant, zone) = await TenantAsync(cancellationToken).ConfigureAwait(false);
        var today = clock.GetCurrentInstant().InZone(zone).Date;
        return await ReadAsync(day ?? today, today, tenant.Currency, zone, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CashUpResult> CloseAsync(LocalDate day, decimal countedCash, string? note, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        if (!TillRules.IsCountable(countedCash))
        {
            throw new ArgumentOutOfRangeException(nameof(countedCash), countedCash, "A count is not negative and has at most two decimals.");
        }

        var (tenant, zone) = await TenantAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        var today = now.InZone(zone).Date;
        var current = await ReadAsync(day, today, tenant.Currency, zone, cancellationToken).ConfigureAwait(false);
        if (day > today)
        {
            return new CashUpResult(CashUpOutcome.FutureDay, current);
        }

        if (current.Closed is not null)
        {
            return new CashUpResult(CashUpOutcome.AlreadyClosed, current);
        }

        var remark = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        var cashUp = new CashUp
        {
            TenantId = tenant.Id,
            Day = day,
            ExpectedCash = current.ExpectedCash.Amount,
            CountedCash = countedCash,
            Currency = tenant.Currency,
            Note = remark is { Length: > CashUp.NoteMaxLength } ? remark[..CashUp.NoteMaxLength] : remark,
            ClosedAt = now,
            ClosedByUserId = byUserId,
        };
        db.CashUps.Add(cashUp);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two people closed the same day at once: the first count stands.
            db.Entry(cashUp).State = EntityState.Detached;
            logger.LogInformation(ex, "Cash-up of {Day} was already closed by another request", day);
            return new CashUpResult(CashUpOutcome.AlreadyClosed, current);
        }

        logger.LogInformation("Cash-up of {Day} closed by user {UserId}: expected {Expected}, counted {Counted} {Currency}", day, byUserId, cashUp.ExpectedCash, cashUp.CountedCash, cashUp.Currency);
        return new CashUpResult(CashUpOutcome.Closed, await ReadAsync(day, today, tenant.Currency, zone, cancellationToken).ConfigureAwait(false));
    }

    private async Task<CashDay> ReadAsync(LocalDate day, LocalDate today, string currency, DateTimeZone zone, CancellationToken cancellationToken)
    {
        var from = zone.AtStartOfDay(day).ToInstant();
        var until = zone.AtStartOfDay(day.PlusDays(1)).ToInstant();

        // Only lines with a method moved money; a fee added or waived is not the till's business.
        var rows = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.Method != null && e.At >= from && e.At < until)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.Kind, Method = e.Method!.Value, e.Amount, e.At, e.ClientId, e.AppointmentId, e.ByUserId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var clientIds = rows.Select(r => r.ClientId).Distinct().ToArray();
        var clients = await db.Clients.AsNoTracking()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken).ConfigureAwait(false);
        var closed = await db.CashUps.AsNoTracking().FirstOrDefaultAsync(c => c.Day == day, cancellationToken).ConfigureAwait(false);
        var userIds = rows.Where(r => r.ByUserId is not null).Select(r => r.ByUserId!.Value)
            .Concat(closed?.ClosedByUserId is { } closer ? [closer] : [])
            .Distinct().ToArray();
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken).ConfigureAwait(false);
        string? Name(Guid? userId) => userId is { } id && users.TryGetValue(id, out var name) ? name : null;
        Money In(decimal amount) => new(amount, currency);

        var lines = rows.Select(r => (r.Kind, r.Method, r.Amount)).ToArray();
        var movements = rows
            .Where(r => TillRules.Direction(r.Kind) != 0)
            .Select(r => new CashMovement(
                r.At.InZone(zone),
                r.Kind,
                r.Method,
                In(TillRules.Direction(r.Kind) * r.Amount),
                clients.TryGetValue(r.ClientId, out var client) ? client : string.Empty,
                r.AppointmentId,
                Name(r.ByUserId)))
            .ToArray();
        var totals = TillRules.Totals(lines).Select(t => new MethodTotal(t.Method, In(t.Amount))).ToArray();

        return new CashDay(
            day,
            today,
            movements,
            totals,
            In(totals.Sum(t => t.Amount.Amount)),
            In(TillRules.ExpectedCash(lines)),
            closed is null
                ? null
                : new CashUpRecord(In(closed.ExpectedCash), In(closed.CountedCash), In(closed.Difference), closed.Note, closed.ClosedAt.InZone(zone), Name(closed.ClosedByUserId)));
    }

    private async Task<(TenantInfo Tenant, DateTimeZone Zone)> TenantAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("The till belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenant, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc);
    }
}