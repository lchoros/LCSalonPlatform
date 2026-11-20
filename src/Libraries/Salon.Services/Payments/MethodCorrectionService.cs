using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Payments;

/// <summary>
/// Puts right a line of money that was recorded under the wrong method (ADR 0064): "I pressed cash, it was card".
/// Nothing is edited and no money moves: two lines are added that take the amount off one method and put it on
/// another, so the day's takings stay the same and the till adds up per method.
/// </summary>
public interface IMethodCorrectionService
{
    /// <summary>
    /// The line as the correction page shows it; null when this salon has no such line, or the line moved no money
    /// (a fee added or waived, a voucher used) or is itself half of a correction.
    /// </summary>
    Task<CorrectableLine?> GetAsync(Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the line <paramref name="entryId"/> to <paramref name="method"/>. Only a line of today (the salon's
    /// day) that moved money can be corrected, and only to a method the salon has that it does not stand at already.
    /// </summary>
    /// <param name="correctionId">The id the form was given; the same form sent twice is one correction.</param>
    Task<MethodCorrectionResult> CorrectAsync(Guid entryId, PaymentChoice method, Guid? byUserId, Guid? correctionId = null, CancellationToken cancellationToken = default);
}

/// <param name="Day">The salon's day of the line: the till it is on.</param>
/// <param name="At">In the salon's zone.</param>
/// <param name="Amount">Signed as on the till: above zero came in, below went out.</param>
/// <param name="ClientName">Empty for a sale to somebody who is not a client.</param>
/// <param name="Recorded">The method on the line itself.</param>
/// <param name="StandsAt">The method after its corrections; the same as <paramref name="Recorded"/> when there are none.</param>
/// <param name="IsToday">Only a line of today can be corrected: a correction counts on the till of the day it is made.</param>
public sealed record CorrectableLine(
    Guid EntryId,
    LocalDate Day,
    ZonedDateTime At,
    ClientAccountEntryKind Kind,
    Money Amount,
    string ClientName,
    ResolvedPaymentMethod Recorded,
    ResolvedPaymentMethod StandsAt,
    bool IsToday)
{
    public bool WasCorrected => Recorded.Method != StandsAt.Method || Recorded.CustomMethodId != StandsAt.CustomMethodId;

    /// <summary>The method the line stands at, as a form posts it.</summary>
    public PaymentChoice Choice => new(StandsAt.Method, StandsAt.CustomMethodId);

    /// <summary>A gift voucher's own money: "voucher" is not among the methods it can be moved to (ADR 0057).</summary>
    public bool IsVoucherMoney => ClientAccountRules.IsVoucher(Kind);
}

/// <param name="Day">The salon's day of the line, to show its till again; null when there is no such line.</param>
/// <param name="Now">The method the line stands at after a correction; null when nothing changed.</param>
public sealed record MethodCorrectionResult(MethodCorrectionOutcome Outcome, LocalDate? Day, ResolvedPaymentMethod? Now);

public enum MethodCorrectionOutcome
{
    Corrected = 1,

    /// <summary>No such line in this salon.</summary>
    NotFound = 2,

    /// <summary>The line moved no money (a fee added or waived, a voucher used) or is itself half of a correction.</summary>
    NotCorrectable = 3,

    /// <summary>The line is of another day: a correction now would change the till of a day that did not take the money.</summary>
    NotToday = 4,

    /// <summary>The line stands at that method already.</summary>
    SameMethod = 5,

    /// <summary>Not a method of this salon, or "voucher" for a gift voucher's own money.</summary>
    MethodNotOffered = 6,

    /// <summary>The form's id belongs to another line: nothing is written.</summary>
    AlreadyBeingRecorded = 7,
}

internal sealed class MethodCorrectionService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IPaymentMethodService methods,
    IClock clock,
    ILogger<MethodCorrectionService> logger) : IMethodCorrectionService
{
    public async Task<CorrectableLine?> GetAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);
        var line = await db.ClientAccountEntries.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entryId, cancellationToken).ConfigureAwait(false);
        if (line?.Method is not { } recorded || !MethodCorrectionRules.CanCorrect(line.Kind, recorded))
        {
            return null;
        }

        var clientName = line.ClientId is { } clientId
            ? await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => c.FirstName + " " + c.LastName).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            : null;
        var on = new ResolvedPaymentMethod(recorded, line.CustomMethodId, line.MethodName);
        var day = line.At.InZone(zone).Date;
        return new CorrectableLine(
            line.Id,
            day,
            line.At.InZone(zone),
            line.Kind,
            new Money(TillRules.Direction(line.Kind) * line.Amount, line.Currency),
            clientName ?? string.Empty,
            on,
            await StandsAtAsync(line.Id, on, cancellationToken).ConfigureAwait(false),
            day == clock.GetCurrentInstant().InZone(zone).Date);
    }

    public async Task<MethodCorrectionResult> CorrectAsync(Guid entryId, PaymentChoice method, Guid? byUserId, Guid? correctionId = null, CancellationToken cancellationToken = default)
    {
        var zone = await ZoneAsync(cancellationToken).ConfigureAwait(false);

        // Its own transaction when the caller has none, so the lock below holds until both lines are written.
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            // Two corrections of one line run one after the other: the second reads where the first left the line.
            if (await LockLineAsync(entryId, cancellationToken).ConfigureAwait(false) == 0)
            {
                return new MethodCorrectionResult(MethodCorrectionOutcome.NotFound, null, null);
            }

            var line = await db.ClientAccountEntries.AsNoTracking().FirstAsync(e => e.Id == entryId, cancellationToken).ConfigureAwait(false);
            var now = clock.GetCurrentInstant();
            var day = line.At.InZone(zone).Date;
            MethodCorrectionResult Refused(MethodCorrectionOutcome outcome) => new(outcome, day, null);

            if (line.Method is not { } recorded || !MethodCorrectionRules.CanCorrect(line.Kind, recorded))
            {
                return Refused(MethodCorrectionOutcome.NotCorrectable);
            }

            var standsAt = await StandsAtAsync(entryId, new ResolvedPaymentMethod(recorded, line.CustomMethodId, line.MethodName), cancellationToken).ConfigureAwait(false);
            if (correctionId is { } sent)
            {
                var again = await db.ClientAccountEntries.AsNoTracking().Where(e => e.Id == sent).Select(e => new { e.Kind, e.RelatedEntryId }).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                if (again is not null)
                {
                    // The same form again (a second click, a reload of the post): one correction. Another line's id is not ours.
                    return ClientAccountRules.IsMethodCorrection(again.Kind) && again.RelatedEntryId == entryId
                        ? new MethodCorrectionResult(MethodCorrectionOutcome.Corrected, day, standsAt)
                        : Refused(MethodCorrectionOutcome.AlreadyBeingRecorded);
                }
            }

            if (day != now.InZone(zone).Date)
            {
                return Refused(MethodCorrectionOutcome.NotToday);
            }

            var paid = MethodCorrectionRules.Allows(line.Kind, method) ? await methods.ResolveAsync(method, cancellationToken).ConfigureAwait(false) : null;
            if (paid is null)
            {
                return Refused(MethodCorrectionOutcome.MethodNotOffered);
            }

            if (paid.Method == standsAt.Method && paid.CustomMethodId == standsAt.CustomMethodId)
            {
                return Refused(MethodCorrectionOutcome.SameMethod);
            }

            var (undoKind, restateKind) = MethodCorrectionRules.Pair(line.Kind);
            var undo = Half(line, undoKind, standsAt with { MethodName = await NameTodayAsync(standsAt, cancellationToken).ConfigureAwait(false) }, now, byUserId);

            // The form's id is the first line's id, so the database itself refuses the same correction twice. The
            // ids sort the way the two lines read, at the same moment: off the one method first, then on the other.
            undo.Id = correctionId ?? Guid.CreateVersion7(now.ToDateTimeOffset());
            var restate = Half(line, restateKind, paid, now, byUserId);
            restate.Id = Guid.CreateVersion7(now.ToDateTimeOffset().AddMilliseconds(1));
            db.ClientAccountEntries.AddRange(undo, restate);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation("Line {EntryId} of kind {Kind} corrected from {From} to {To} by user {UserId}", line.Id, line.Kind, standsAt.Method, paid.Method, byUserId ?? Guid.Empty);
            return new MethodCorrectionResult(MethodCorrectionOutcome.Corrected, day, paid);
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
    /// Takes the row lock on the line without changing it: the amount is written back as it is. A second correction
    /// of the same line waits here until the first has committed. Returns how many rows it found (none: no such line).
    /// </summary>
    private Task<int> LockLineAsync(Guid entryId, CancellationToken cancellationToken)
        => db.ClientAccountEntries.Where(e => e.Id == entryId).ExecuteUpdateAsync(s => s.SetProperty(e => e.Amount, e => e.Amount), cancellationToken);

    private async Task<ResolvedPaymentMethod> StandsAtAsync(Guid entryId, ResolvedPaymentMethod recorded, CancellationToken cancellationToken)
        => (await MethodCorrections.LatestAsync(db, [entryId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(entryId) ?? recorded;

    /// <summary>
    /// What the salon calls its own method today, taken out of use or not: the till shows each of the salon's methods
    /// under the name on its latest line (ADR 0060), and the line that undoes is the latest.
    /// </summary>
    private async Task<string?> NameTodayAsync(ResolvedPaymentMethod method, CancellationToken cancellationToken)
        => method.CustomMethodId is { } id
            ? await db.SalonPaymentMethods.AsNoTracking().Where(m => m.Id == id).Select(m => m.Name).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false) ?? method.MethodName
            : null;

    private async Task<DateTimeZone> ZoneAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A correction belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
    }

    /// <summary>One of the two lines: the amount, the client and the visit of the line it corrects, under one method.</summary>
    private static ClientAccountEntry Half(ClientAccountEntry line, ClientAccountEntryKind kind, ResolvedPaymentMethod method, Instant at, Guid? byUserId)
        => new()
        {
            TenantId = line.TenantId,
            ClientId = line.ClientId,
            AppointmentId = line.AppointmentId,
            Kind = kind,
            Amount = line.Amount,
            Currency = line.Currency,
            Method = method.Method,
            CustomMethodId = method.CustomMethodId,
            MethodName = method.MethodName,
            RelatedEntryId = line.Id,
            At = at,
            ByUserId = byUserId,
        };
}

/// <summary>Where lines stand after their corrections (ADR 0064), for every page that says how something was paid.</summary>
internal static class MethodCorrections
{
    /// <summary>
    /// The method each of <paramref name="entryIds"/> was last corrected to; a line nobody corrected is not in the
    /// answer and stands at the method on the line itself.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, ResolvedPaymentMethod>> LatestAsync(SalonDbContext db, IReadOnlyCollection<Guid> entryIds, CancellationToken cancellationToken)
    {
        var latest = new Dictionary<Guid, ResolvedPaymentMethod>();
        if (entryIds.Count == 0)
        {
            return latest;
        }

        var rows = await (
            from correction in db.ClientAccountEntries.AsNoTracking()
            join line in db.ClientAccountEntries.AsNoTracking() on correction.RelatedEntryId equals line.Id
            where entryIds.Contains(line.Id)
                && (correction.Kind == ClientAccountEntryKind.MethodCorrectionIn || correction.Kind == ClientAccountEntryKind.MethodCorrectionOut)
                && correction.Method != null
            orderby correction.At, correction.Id
            select new { line.Id, Original = line.Kind, correction.Kind, Method = correction.Method!.Value, correction.CustomMethodId, correction.MethodName })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        // Oldest first, so the last one written for a line is the one left in the dictionary.
        foreach (var row in rows.Where(r => MethodCorrectionRules.Restates(r.Original, r.Kind)))
        {
            latest[row.Id] = new ResolvedPaymentMethod(row.Method, row.CustomMethodId, row.MethodName);
        }

        return latest;
    }

    /// <summary>The method a line stands at: its latest correction, or what was recorded on it.</summary>
    public static ResolvedPaymentMethod? Of(this IReadOnlyDictionary<Guid, ResolvedPaymentMethod> latest, Guid entryId, PaymentMethod? method, Guid? customMethodId, string? methodName)
        => latest.TryGetValue(entryId, out var corrected) ? corrected : method is { } recorded ? new ResolvedPaymentMethod(recorded, customMethodId, methodName) : null;
}