using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Clients;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Security;
using Salon.Services.Tenancy;

namespace Salon.Services.Clients;

/// <summary>A consultation as a list shows it: when, who took it and for what. Nothing about health.</summary>
/// <param name="TakenOn">In the salon's zone.</param>
/// <param name="TakenByName">Who took it; null when the login is gone or unknown.</param>
public sealed record ConsultationSummary(Guid Id, LocalDate TakenOn, string? TakenByName, string? For);

/// <param name="CanRecord">False for an inactive card or one merged into another: its forms can be read, none can be added.</param>
public sealed record ConsultationClient(Guid Id, string FullName, bool CanRecord);

/// <summary>A consultation opened to be read (ADR 0058).</summary>
/// <param name="Answers">The decrypted answers; null when the current key ring cannot open them.</param>
/// <param name="Unreadable">True when the cipher text could not be opened: the page says so instead of showing nothing.</param>
/// <param name="ConsentWording">The sentence the client agreed to.</param>
public sealed record ConsultationDetails(ConsultationSummary Summary, Guid ClientId, string ClientName, ConsultationAnswers? Answers, bool Unreadable, string ConsentWording);

/// <param name="ConsentWording">The sentence shown beside the tick, as the client saw it.</param>
/// <param name="Agreed">The tick.</param>
/// <param name="RecordId">The id the form was given; the same form sent twice is one record.</param>
public sealed record ConsultationRequest(
    Guid ClientId,
    string? For,
    IReadOnlyDictionary<string, bool>? Answers,
    string? Details,
    LocalDate? PatchTestOn,
    string? ConfirmedBy,
    bool Agreed,
    string ConsentWording,
    Guid? RecordId = null)
{
    /// <summary>Never the answers: a request must not end up in a log line because somebody printed it.</summary>
    public override string ToString() => $"{nameof(ConsultationRequest)} {{ ClientId = {ClientId} }}";
}

public enum ConsultationOutcome
{
    Recorded = 1,

    /// <summary>The salon has no such client, or the card is inactive or merged away.</summary>
    ClientUnknown = 2,

    /// <summary>A question has no answer.</summary>
    Incomplete = 3,

    /// <summary>The client has not typed their name, or has not ticked the box.</summary>
    NotConfirmed = 4,

    /// <summary>The details, or what it is for, are too long.</summary>
    TooLong = 5,

    /// <summary>The patch test is dated in the future.</summary>
    PatchTestDayInvalid = 6,
}

public sealed record ConsultationResult(ConsultationOutcome Outcome, Guid? Id = null);

/// <summary>
/// Consultation forms (ADR 0058): what a client said about their health before a treatment, kept encrypted with the
/// same field encryptor as the health notes on their card (ADR 0011). Only this service turns the answers into cipher
/// text and back; lists carry no answers; log lines carry ids only.
/// </summary>
public interface IConsultationService
{
    /// <summary>
    /// Whose form it would be: the name for the top of the page and whether a form can be taken (an active card that
    /// was not merged away). Null when the salon has no such client. Reads no health data.
    /// </summary>
    Task<ConsultationClient?> FindClientAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>Keeps a consultation. A patch test dated later than the one on the card moves the card's date on.</summary>
    Task<ConsultationResult> RecordAsync(ConsultationRequest request, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>The client's consultations, newest first. No answers.</summary>
    Task<IReadOnlyList<ConsultationSummary>> ListAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>One consultation with its answers; null when the salon has none with this id for this client.</summary>
    Task<ConsultationDetails?> GetAsync(Guid clientId, Guid consultationId, CancellationToken cancellationToken = default);
}

internal sealed class ConsultationService(
    SalonDbContext db,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IFieldEncryptor encryptor,
    IClock clock,
    ILogger<ConsultationService> logger) : IConsultationService
{
    public async Task<ConsultationResult> RecordAsync(ConsultationRequest request, Guid? byUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (tenantId, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.GetCurrentInstant();
        if (request.RecordId is { } sent)
        {
            var again = await db.ConsultationRecords.AsNoTracking()
                .Where(r => r.Id == sent && r.ClientId == request.ClientId)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (again is not null)
            {
                // The same form again (a second click, a reload of the post): one record.
                return new ConsultationResult(ConsultationOutcome.Recorded, again);
            }
        }

        if (!ConsultationRules.IsComplete(request.Answers))
        {
            return new ConsultationResult(ConsultationOutcome.Incomplete);
        }

        if (!ConsultationRules.IsConfirmed(request.ConfirmedBy, request.Agreed) || string.IsNullOrWhiteSpace(request.ConsentWording))
        {
            return new ConsultationResult(ConsultationOutcome.NotConfirmed);
        }

        if (!ConsultationRules.DetailsFit(request.Details) || !ConsultationRules.ForFits(request.For) || request.ConsentWording.Length > ConsultationRecord.ConsentWordingMaxLength)
        {
            return new ConsultationResult(ConsultationOutcome.TooLong);
        }

        if (!ConsultationRules.PatchTestDayFits(request.PatchTestOn, now.InZone(zone).Date))
        {
            return new ConsultationResult(ConsultationOutcome.PatchTestDayInvalid);
        }

        var client = await db.Clients.FirstOrDefaultAsync(c => c.Id == request.ClientId && c.IsActive && c.MergedIntoClientId == null, cancellationToken).ConfigureAwait(false);
        if (client is null)
        {
            return new ConsultationResult(ConsultationOutcome.ClientUnknown);
        }

        // Only the questions of the form are kept; anything else a caller sent along is dropped.
        var answers = ConsultationQuestions.All.ToDictionary(key => key, key => request.Answers![key]);
        var stored = new StoredAnswers(
            answers,
            string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim(),
            request.PatchTestOn is { } day ? LocalDatePattern.Iso.Format(day) : null,
            request.ConfirmedBy!.Trim());
        var record = new ConsultationRecord
        {
            Id = request.RecordId ?? Guid.CreateVersion7(),
            TenantId = tenantId,
            ClientId = client.Id,
            TakenAt = now,
            TakenByUserId = byUserId,
            For = string.IsNullOrWhiteSpace(request.For) ? null : request.For.Trim(),
            AnswersProtected = encryptor.Protect(JsonSerializer.Serialize(stored, StoredOptions)),
            ConsentWording = request.ConsentWording.Trim(),
            FormVersion = ConsultationQuestions.Version,
        };
        db.ConsultationRecords.Add(record);

        // The card's patch test date follows the newest test, so the desk sees it without opening the form.
        if (request.PatchTestOn is { } tested && (client.LastPatchTestOn is null || tested > client.LastPatchTestOn))
        {
            client.LastPatchTestOn = tested;
            client.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Ids only: who recorded a consultation for whom, never what was said. The user id is written as a plain id
        // (all zeros for nobody): it travels here beside what the form posted, and a log line takes only values
        // whose type cannot carry text (ADR 0060 amendment).
        logger.LogInformation("Consultation {ConsultationId} recorded for client {ClientId} by user {UserId}", record.Id, client.Id, byUserId ?? Guid.Empty);
        return new ConsultationResult(ConsultationOutcome.Recorded, record.Id);
    }

    public async Task<ConsultationClient?> FindClientAsync(Guid clientId, CancellationToken cancellationToken = default)
        => await db.Clients.AsNoTracking()
            .Where(c => c.Id == clientId)
            .Select(c => new ConsultationClient(c.Id, c.FirstName + " " + c.LastName, c.IsActive && c.MergedIntoClientId == null))
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ConsultationSummary>> ListAsync(Guid clientId, CancellationToken cancellationToken = default)
    {
        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.ConsultationRecords.AsNoTracking()
            .Where(r => r.ClientId == clientId)
            .OrderByDescending(r => r.TakenAt).ThenByDescending(r => r.Id)
            .Select(r => new { r.Id, r.TakenAt, r.TakenByUserId, r.For })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var users = await NamesAsync(rows.Select(r => r.TakenByUserId), cancellationToken).ConfigureAwait(false);
        return [.. rows.Select(r => new ConsultationSummary(r.Id, r.TakenAt.InZone(zone).Date, Name(users, r.TakenByUserId), r.For))];
    }

    public async Task<ConsultationDetails?> GetAsync(Guid clientId, Guid consultationId, CancellationToken cancellationToken = default)
    {
        var record = await db.ConsultationRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == consultationId && r.ClientId == clientId, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return null;
        }

        var (_, zone) = await SalonAsync(cancellationToken).ConfigureAwait(false);
        var client = await db.Clients.AsNoTracking().Where(c => c.Id == clientId).Select(c => new { c.FirstName, c.LastName }).FirstAsync(cancellationToken).ConfigureAwait(false);
        var users = await NamesAsync([record.TakenByUserId], cancellationToken).ConfigureAwait(false);
        var summary = new ConsultationSummary(record.Id, record.TakenAt.InZone(zone).Date, Name(users, record.TakenByUserId), record.For);

        ConsultationAnswers? answers = null;
        if (encryptor.TryUnprotect(record.AnswersProtected, out var plain) && plain is not null)
        {
            var stored = Read(plain);
            if (stored is not null)
            {
                answers = new ConsultationAnswers(
                    stored.Answers,
                    stored.Details,
                    stored.PatchTestOn is { } day && LocalDatePattern.Iso.Parse(day) is { Success: true } parsed ? parsed.Value : null,
                    stored.ConfirmedBy);
            }
        }

        // Ids only: who opened which consultation. Reading health data leaves a trace.
        logger.LogInformation("Consultation {ConsultationId} of client {ClientId} opened", record.Id, clientId);
        return new ConsultationDetails(summary, clientId, client.FirstName + " " + client.LastName, answers, answers is null, record.ConsentWording);
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => id is not null).Select(id => id!.Value).Distinct().ToArray();
        return ids.Length == 0
            ? []
            : await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken).ConfigureAwait(false);
    }

    private static string? Name(Dictionary<Guid, string> users, Guid? userId) => userId is { } id && users.TryGetValue(id, out var name) ? name : null;

    private async Task<(Guid TenantId, DateTimeZone Zone)> SalonAsync(CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Consultations belong to a salon, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return (tenantId, DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc);
    }

    /// <summary>
    /// Letters are written as themselves, not as escape codes: the document is encrypted, never put into a page, and a
    /// note in Polish would otherwise be six times its length.
    /// </summary>
    private static readonly JsonSerializerOptions StoredOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Text that opened but is not the document (a record damaged at rest) is unreadable, not an error page.</summary>
    private static StoredAnswers? Read(string plain)
    {
        try
        {
            return JsonSerializer.Deserialize<StoredAnswers>(plain, StoredOptions) is { Answers: not null, ConfirmedBy: not null } stored ? stored : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The document that is encrypted. Plain strings and booleans only, so it reads the same in ten years.</summary>
    private sealed record StoredAnswers(Dictionary<string, bool> Answers, string? Details, string? PatchTestOn, string ConfirmedBy);
}