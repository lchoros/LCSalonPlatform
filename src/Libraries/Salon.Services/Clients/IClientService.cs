using NodaTime;
using Salon.Core.Appointments;

namespace Salon.Services.Clients;

/// <summary>
/// The current salon's client cards (ADR 0011). Health notes are special-category data: they leave the database decrypted
/// only through <see cref="GetCardAsync"/>; <see cref="SearchAsync"/> never reads the column and nothing here logs or
/// publishes them.
/// </summary>
public interface IClientService
{
    /// <summary>
    /// Creates a client in the current tenant and publishes <see cref="Salon.Core.Clients.Events.ClientCreated"/>. Phone
    /// and email are normalised (the tenant's currency hints the country for local numbers); a duplicate phone or email
    /// within the tenant throws <see cref="DuplicateClientException"/>, invalid input <see cref="ArgumentException"/>.
    /// With <see cref="CreateClientRequest.MarketingConsent"/> the wording shown and the current instant are recorded.
    /// </summary>
    Task<Guid> CreateAsync(CreateClientRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Case-insensitive prefix match on first or last name (every word of the query must match one of them), or a digits
    /// match on the normalised phone when the query is a number; a blank query lists the first clients by name. Ordered by
    /// last name then first name, at most <paramref name="take"/> rows, never with health notes.
    /// </summary>
    Task<IReadOnlyList<ClientSummary>> SearchAsync(string? query, int take = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// The client whose stored phone equals <paramref name="phone"/> once normalised with the salon's country (ADR 0011), or
    /// null. The public booking flow (ADR 0014) uses it to recognise a returning client; an unusable number is null too.
    /// </summary>
    Task<ClientSummary?> FindByPhoneAsync(string? phone, CancellationToken cancellationToken = default);

    /// <summary>The full card including decrypted health notes; null when the id is not a client of the current tenant.</summary>
    Task<ClientCard?> GetCardAsync(Guid clientId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the health notes (encrypted at rest; null clears them) and the last patch-test date.</summary>
    Task UpdateHealthNotesAsync(Guid clientId, string? healthNotes, LocalDate? lastPatchTestOn, CancellationToken cancellationToken = default);

    /// <summary>Grants consent with the exact wording shown and the current instant, or withdraws it and clears both.</summary>
    Task SetMarketingConsentAsync(Guid clientId, bool granted, string? wording, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the client's own deposit percentage (ADR 0023): null follows the service and the salon, 0 never asks for a
    /// deposit. Throws <see cref="ArgumentOutOfRangeException"/> outside 0 to 100.
    /// </summary>
    Task SetDepositPercentAsync(Guid clientId, decimal? percent, CancellationToken cancellationToken = default);

    /// <summary>Updates client details (name, phone, email, notes) ensuring uniqueness within the tenant.</summary>
    Task UpdateDetailsAsync(Guid clientId, string firstName, string lastName, string? phone, string? email, string? notes, CancellationToken cancellationToken = default);

    /// <summary>
    /// The client's visits (ADR 0017): up to <paramref name="take"/> upcoming (active, starting from now, soonest first) and
    /// up to <paramref name="take"/> past (everything else, newest first) with service and staff names resolved through the
    /// tenant filter and starts in the salon's zone, plus counters over every visit. Null when the id is not a client of
    /// the current tenant.
    /// </summary>
    Task<ClientHistory?> GetHistoryAsync(Guid clientId, int take = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Merges the duplicate <paramref name="sourceId"/> into <paramref name="targetId"/> in one transaction (ADR 0017,
    /// <see cref="Salon.Core.Clients.ClientMergeRules"/>): the source's appointments (and so their status history) move to
    /// the target, notes are joined, health notes, patch test, phone and email of the target are kept and only filled from
    /// the source where empty, consent is never gained, and the source becomes inactive with
    /// <see cref="Salon.Core.Clients.Client.MergedIntoClientId"/> set and its contact details cleared (they are unique per
    /// salon). Publishes <see cref="Salon.Core.Clients.Events.ClientsMerged"/>. An id the tenant does not own throws
    /// <see cref="InvalidOperationException"/>; self-merge or an already merged card <see cref="ClientMergeRefusedException"/>.
    /// </summary>
    Task MergeAsync(Guid targetId, Guid sourceId, Guid? byUserId, CancellationToken cancellationToken = default);
}

/// <summary>What the create form collects. Text is trimmed and normalised by the service.</summary>
/// <param name="HealthNotes">Plain text here, for this request only; stored encrypted.</param>
/// <param name="MarketingConsentWording">The exact text the client ticked; required when <paramref name="MarketingConsent"/> is true.</param>
public sealed record CreateClientRequest(
    string FirstName,
    string LastName,
    string? Phone = null,
    string? Email = null,
    string? Notes = null,
    string? HealthNotes = null,
    LocalDate? LastPatchTestOn = null,
    bool MarketingConsent = false,
    string? MarketingConsentWording = null)
{
    /// <summary>Keeps contact details and health notes out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(CreateClientRequest)} {{ FirstName = {FirstName}, LastName = {LastName} }}";
}

/// <summary>One search result: enough to recognise and call the client, nothing sensitive.</summary>
public sealed record ClientSummary(Guid Id, string FirstName, string LastName, string? Phone, string? Email, bool IsActive)
{
    public string FullName => $"{FirstName} {LastName}";
}

/// <summary>The full card. <see cref="HealthNotes"/> is decrypted; <see cref="HealthNotesUnreadable"/> flags cipher text the current key ring cannot open.</summary>
public sealed record ClientCard(
    Guid Id,
    string FirstName,
    string LastName,
    string? Phone,
    string? Email,
    string? Notes,
    string? HealthNotes,
    bool HealthNotesUnreadable,
    LocalDate? LastPatchTestOn,
    bool MarketingConsent,
    Instant? MarketingConsentAt,
    string? MarketingConsentWording,
    bool IsActive,
    Instant CreatedAt,
    Instant UpdatedAt,
    Guid? MergedIntoClientId = null,
    Instant? MergedAt = null,
    decimal? DepositPercent = null)
{
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>True once the card was merged into another (ADR 0017); the card then only points at the target.</summary>
    public bool IsMerged => MergedIntoClientId is not null;

    /// <summary>Never prints the health notes.</summary>
    public override string ToString() => $"{nameof(ClientCard)} {{ Id = {Id} }}";
}

/// <summary>A client's visits (ADR 0017). Starts carry the salon's zone; the counters cover every appointment, not only the rows listed.</summary>
/// <param name="Upcoming">Active visits from now on, soonest first.</param>
/// <param name="Past">Everything else (completed, no-show, cancelled, or already started), newest first.</param>
/// <param name="VisitsTotal">Every appointment row of the client, whatever its status.</param>
/// <param name="LastVisitOn">The day of the most recent completed visit, in the salon's zone.</param>
public sealed record ClientHistory(
    IReadOnlyList<ClientVisit> Upcoming,
    IReadOnlyList<ClientVisit> Past,
    int VisitsTotal,
    int VisitsCompleted,
    int NoShows,
    LocalDate? LastVisitOn);

/// <summary>One row of the history: what, with whom, when (salon zone), how it ended and the price as booked.</summary>
public sealed record ClientVisit(
    Guid AppointmentId,
    ZonedDateTime Start,
    int DurationMinutes,
    string ServiceName,
    string StaffName,
    AppointmentStatus Status,
    decimal PriceAmount,
    string Currency);