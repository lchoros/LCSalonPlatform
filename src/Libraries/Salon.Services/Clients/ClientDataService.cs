using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Clients;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Services.Tenancy;

namespace Salon.Services.Clients;

/// <summary>Everything the salon holds about one client (ADR 0062), for the copy a client may ask for.</summary>
/// <param name="Card">Their details, notes, health notes (decrypted) and consent.</param>
/// <param name="MadeAt">When this copy was made, in the salon's zone.</param>
/// <param name="Visits">Every visit, oldest first, whatever became of it.</param>
/// <param name="Money">Every line on their account, oldest first: fees, deposits, payments.</param>
/// <param name="Products">Every product on their bills, oldest first.</param>
/// <param name="Consultations">Every consultation form with its answers, oldest first.</param>
/// <param name="Waitlist">Every time they asked to be told when a time comes up, oldest first.</param>
/// <param name="MergedCards">How many duplicate cards were merged into this one; their content is in this copy.</param>
public sealed record ClientDataCopy(
    ClientCard Card,
    ZonedDateTime MadeAt,
    IReadOnlyList<CopyVisit> Visits,
    IReadOnlyList<CopyMoneyLine> Money,
    IReadOnlyList<CopyProduct> Products,
    IReadOnlyList<ConsultationDetails> Consultations,
    IReadOnlyList<CopyWaitlistEntry> Waitlist,
    int MergedCards)
{
    /// <summary>Never the content: a copy must not end up in a log line because somebody printed it.</summary>
    public override string ToString() => $"{nameof(ClientDataCopy)} {{ ClientId = {Card.Id} }}";
}

/// <param name="Start">In the salon's zone.</param>
/// <param name="Notes">What the desk wrote on the visit.</param>
public sealed record CopyVisit(ZonedDateTime Start, string ServiceName, string StaffName, AppointmentStatus Status, Money Price, AppointmentSource Source, string? Notes, string? CancellationReason);

/// <param name="At">In the salon's zone.</param>
/// <param name="Amount">Always above zero; the kind says whether it came in, went out or moved no money.</param>
/// <param name="MethodName">The salon's own method by its name; null for a built-in method or a line without one.</param>
public sealed record CopyMoneyLine(ZonedDateTime At, ClientAccountEntryKind Kind, Money Amount, PaymentMethod? Method, string? MethodName, string? Note);

/// <param name="AddedAt">In the salon's zone.</param>
public sealed record CopyProduct(ZonedDateTime AddedAt, string Name, int Quantity, int Returned, Money UnitPrice, bool Paid);

/// <param name="CreatedOn">The day they asked, in the salon's zone.</param>
public sealed record CopyWaitlistEntry(LocalDate CreatedOn, string ServiceName, string? StaffName, LocalDate? NotBefore, LocalDate? NotAfter, string? Note, WaitlistOutcome? Outcome);

/// <summary>What erasing a client would do, shown before the owner confirms (ADR 0062).</summary>
/// <param name="Blockers">What must be settled first; empty when the client can be erased now.</param>
/// <param name="VisitsKept">Visits that stay, without their notes, pointing at a row with no name.</param>
/// <param name="MoneyLinesKept">Lines of the client's account that stay: the salon's books.</param>
/// <param name="ConsultationsRemoved">Consultation forms that go.</param>
/// <param name="WaitlistEntriesRemoved">Waitlist requests that go.</param>
public sealed record ErasurePreview(
    Guid ClientId,
    string ClientName,
    IReadOnlyList<ErasureBlocker> Blockers,
    int VisitsKept,
    int MoneyLinesKept,
    int ConsultationsRemoved,
    int WaitlistEntriesRemoved)
{
    public bool CanErase => Blockers.Count == 0;
}

public enum ErasureOutcome
{
    Erased = 1,

    /// <summary>The salon has no such client.</summary>
    NotFound = 2,

    /// <summary>Something must be settled first; see the blockers.</summary>
    Blocked = 3,
}

public sealed record ErasureResult(ErasureOutcome Outcome, IReadOnlyList<ErasureBlocker> Blockers);

/// <summary>
/// A client's rights over their data (ADR 0062): a copy of everything the salon holds about them, and being
/// forgotten. Erasing takes away everything that says who the person is or anything about them, in every table; it
/// keeps the visits and the lines of money, which the salon needs for its books, pointing at a row without a name.
/// Log lines carry ids only.
/// </summary>
public interface IClientDataService
{
    /// <summary>Everything held about the client; null when the salon has no such client. Leaves a trace of who made it.</summary>
    Task<ClientDataCopy?> GetCopyAsync(Guid clientId, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>What erasing would remove and keep, and what stands in the way; null when the salon has no such client.</summary>
    Task<ErasurePreview?> PreviewErasureAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>Erases the client. Not undone by the system. Refused while something must be settled first.</summary>
    Task<ErasureResult> EraseAsync(Guid clientId, Guid? byUserId, CancellationToken cancellationToken = default);
}

internal sealed class ClientDataService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IClientService clients,
    IClientAccountService accounts,
    IConsultationService consultations,
    ILocalizationService localizer,
    IClock clock,
    ILogger<ClientDataService> logger) : IClientDataService
{
    /// <summary>The resource keys of the words an erased client's row carries in place of a name.</summary>
    public const string ErasedFirstNameKey = "Clients.Erased.FirstName";
    public const string ErasedLastNameKey = "Clients.Erased.LastName";

    public async Task<ClientDataCopy?> GetCopyAsync(Guid clientId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var (_, zone, _) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var card = await clients.GetCardAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (card is null)
        {
            return null;
        }

        var visitRows = await (
            from a in db.Appointments.AsNoTracking()
            join s in db.Services.AsNoTracking() on a.ServiceId equals s.Id
            join m in db.StaffMembers.AsNoTracking() on a.StaffMemberId equals m.Id
            where a.ClientId == clientId
            orderby a.Start, a.Id
            select new { a.Start, Service = s.Name, Staff = m.DisplayName, a.Status, a.PriceAmount, a.Currency, a.Source, a.Notes, a.CancellationReason })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var moneyRows = await db.ClientAccountEntries.AsNoTracking()
            .Where(e => e.ClientId == clientId)
            .OrderBy(e => e.At).ThenBy(e => e.Id)
            .Select(e => new { e.At, e.Kind, e.Amount, e.Currency, e.Method, e.MethodName, e.Note })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var productRows = await (
            from l in db.VisitProductLines.AsNoTracking()
            join a in db.Appointments.AsNoTracking() on l.AppointmentId equals a.Id
            where a.ClientId == clientId
            orderby l.AddedAt, l.Id
            select new { l.AddedAt, l.ProductName, l.Quantity, l.ReturnedQuantity, l.UnitPrice, l.Currency, Paid = l.PaymentEntryId != null })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var forms = new List<ConsultationDetails>();
        foreach (var summary in (await consultations.ListAsync(clientId, cancellationToken).ConfigureAwait(false)).Reverse())
        {
            if (await consultations.GetAsync(clientId, summary.Id, cancellationToken).ConfigureAwait(false) is { } form)
            {
                forms.Add(form);
            }
        }

        var waitRows = await (
            from w in db.WaitlistEntries.AsNoTracking()
            join s in db.Services.AsNoTracking() on w.ServiceId equals s.Id
            where w.ClientId == clientId
            orderby w.CreatedAt, w.Id
            select new { w.CreatedAt, Service = s.Name, w.StaffMemberId, w.NotBefore, w.NotAfter, w.Note, w.Outcome })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var staffIds = waitRows.Where(w => w.StaffMemberId is not null).Select(w => w.StaffMemberId!.Value).Distinct().ToArray();
        var staff = await db.StaffMembers.AsNoTracking()
            .Where(m => staffIds.Contains(m.Id))
            .ToDictionaryAsync(m => m.Id, m => m.DisplayName, cancellationToken).ConfigureAwait(false);
        var merged = await db.Clients.CountAsync(c => c.MergedIntoClientId == clientId, cancellationToken).ConfigureAwait(false);

        // Ids only: who made a copy of whose data. The copy holds health data, so making one leaves a trace.
        logger.LogInformation("Copy of the data of client {ClientId} made by user {UserId}", clientId, byUserId ?? Guid.Empty);
        return new ClientDataCopy(
            card,
            clock.GetCurrentInstant().InZone(zone),
            [.. visitRows.Select(v => new CopyVisit(v.Start.InZone(zone), v.Service, v.Staff, v.Status, new Money(v.PriceAmount, v.Currency), v.Source, v.Notes, v.CancellationReason))],
            [.. moneyRows.Select(e => new CopyMoneyLine(e.At.InZone(zone), e.Kind, new Money(e.Amount, e.Currency), e.Method, e.MethodName, e.Note))],
            [.. productRows.Select(p => new CopyProduct(p.AddedAt.InZone(zone), p.ProductName, p.Quantity, p.ReturnedQuantity, new Money(p.UnitPrice, p.Currency), p.Paid))],
            forms,
            [.. waitRows.Select(w => new CopyWaitlistEntry(w.CreatedAt.InZone(zone).Date, w.Service, w.StaffMemberId is { } id ? staff.GetValueOrDefault(id) : null, w.NotBefore, w.NotAfter, w.Note, w.Outcome))],
            merged);
    }

    public async Task<ErasurePreview?> PreviewErasureAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        await SalonAsync(cancellationToken).ConfigureAwait(false);
        var client = await db.Clients.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return null;
        }

        return new ErasurePreview(
            client.Id,
            client.FullName,
            await BlockersAsync(client, cancellationToken).ConfigureAwait(false),
            await db.Appointments.CountAsync(a => a.ClientId == clientId, cancellationToken).ConfigureAwait(false),
            await db.ClientAccountEntries.CountAsync(e => e.ClientId == clientId, cancellationToken).ConfigureAwait(false),
            await db.ConsultationRecords.CountAsync(r => r.ClientId == clientId, cancellationToken).ConfigureAwait(false),
            await db.WaitlistEntries.CountAsync(w => w.ClientId == clientId, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ErasureResult> EraseAsync(Guid clientId, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        var (tenantId, _, culture) = await SalonAsync(cancellationToken).ConfigureAwait(false);

        // Its own transaction when the caller has none: every table changes or none does.
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false) : null;
        try
        {
            var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == clientId, cancellationToken).ConfigureAwait(false);
            if (client is null)
            {
                return new ErasureResult(ErasureOutcome.NotFound, []);
            }

            var blockers = await BlockersAsync(client, cancellationToken).ConfigureAwait(false);
            if (blockers.Count > 0)
            {
                return new ErasureResult(ErasureOutcome.Blocked, blockers);
            }

            var now = clock.GetCurrentInstant();
            var phone = client.Phone;
            var firstName = localizer.Get(ErasedFirstNameKey, culture);
            var lastName = localizer.Get(ErasedLastNameKey, culture);

            // Health data first: what the client said before a colour (ADR 0058).
            var forms = await db.ConsultationRecords.Where(r => r.ClientId == clientId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // What they were waiting for, and what they typed on the booking pages (ADR 0041, ADR 0014).
            var waiting = await db.WaitlistEntries.Where(w => w.ClientId == clientId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
            await db.PhoneVerifications
                .Where(v => (phone != null && v.PhoneE164 == phone) || (v.AppointmentId != null && db.Appointments.Any(a => a.Id == v.AppointmentId && a.ClientId == clientId)))
                .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

            // The visits stay as the record that work was done and paid for; what the desk wrote about the person,
            // and the link the client could open, go.
            var visits = await db.Appointments
                .Where(a => a.ClientId == clientId)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(a => a.Notes, (string?)null).SetProperty(a => a.CancellationReason, (string?)null).SetProperty(a => a.PublicToken, (string?)null),
                    cancellationToken)
                .ConfigureAwait(false);

            // Duplicates merged into this card (ADR 0017) kept their names: they are the same person.
            var duplicates = await db.Clients.Where(c => c.MergedIntoClientId == clientId).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var duplicate in duplicates)
            {
                ClientErasureRules.Scrub(duplicate, firstName, lastName, now, byUserId);
            }

            ClientErasureRules.Scrub(client, firstName, lastName, now, byUserId);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (own is not null)
            {
                await own.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            // Ids and counts only: who erased whom, and how much went. Never what it was.
            logger.LogInformation(
                "Client {ClientId} of tenant {TenantId} erased by user {UserId}: {Forms} consultation forms and {Waiting} waitlist entries removed, {Visits} visits kept without notes, {Duplicates} merged cards cleared",
                clientId,
                tenantId,
                byUserId ?? Guid.Empty,
                forms,
                waiting,
                visits,
                duplicates.Count);
            return new ErasureResult(ErasureOutcome.Erased, []);
        }
        finally
        {
            if (own is not null)
            {
                await own.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private async Task<IReadOnlyList<ErasureBlocker>> BlockersAsync(Client client, CancellationToken cancellationToken)
    {
        if (client.ErasedAt is not null || client.MergedIntoClientId is not null)
        {
            return ClientErasureRules.Blockers(client.ErasedAt is not null, client.MergedIntoClientId is not null, 0, false, false);
        }

        var now = clock.GetCurrentInstant();
        var active = AppointmentStateMachine.Active.ToArray();
        var upcoming = await db.Appointments.CountAsync(a => a.ClientId == client.Id && active.Contains(a.Status) && a.End > now, cancellationToken).ConfigureAwait(false);
        var account = await accounts.GetAccountAsync(client.Id, cancellationToken).ConfigureAwait(false);
        return ClientErasureRules.Blockers(false, false, upcoming, account.Deposits.Any(d => d.IsHeld), account.Owed is not null);
    }

    private async Task<(Guid TenantId, DateTimeZone Zone, string Culture)> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("A client's data belongs to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenantId, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc, tenant.Culture);
    }
}