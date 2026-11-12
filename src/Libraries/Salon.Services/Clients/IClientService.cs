using NodaTime;

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

    /// <summary>Updates client details (name, phone, email, notes) ensuring uniqueness within the tenant.</summary>
    Task UpdateDetailsAsync(Guid clientId, string firstName, string lastName, string? phone, string? email, string? notes, CancellationToken cancellationToken = default);
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
    Instant UpdatedAt)
{
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>Never prints the health notes.</summary>
    public override string ToString() => $"{nameof(ClientCard)} {{ Id = {Id} }}";
}