using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Clients;

/// <summary>
/// A salon's client: the card every appointment hangs off (ADR 0011). Tenant-owned. Phone and email are stored
/// normalised (<c>+447700900123</c>, lower-case email) and are unique per salon when present. Health notes are
/// special-category data under GDPR/RODO: <see cref="HealthNotesProtected"/> holds cipher text only, produced and read
/// through the field encryptor by the client service, never by a list, search, log or event.
/// </summary>
public class Client : BaseEntity, ITenantEntity
{
    public const int NameMaxLength = 100;
    public const int PhoneMaxLength = 20;
    public const int EmailMaxLength = 254;
    public const int NotesMaxLength = 4000;

    /// <summary>Plain-text ceiling for health notes; the cipher text column (<see cref="HealthNotesProtectedMaxLength"/>) is sized for it.</summary>
    public const int HealthNotesMaxLength = 2000;
    public const int HealthNotesProtectedMaxLength = 8000;
    public const int MarketingConsentWordingMaxLength = 500;

    public Guid TenantId { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    /// <summary>E.164-style: a leading <c>+</c> followed by digits only; searched by digits.</summary>
    public string? Phone { get; set; }

    /// <summary>Stored lower-case.</summary>
    public string? Email { get; set; }

    /// <summary>General notes (preferences, how they like their coffee). Plain text.</summary>
    public string? Notes { get; set; }

    /// <summary>Allergies, patch-test outcomes, conditions — encrypted at rest; see the class remarks.</summary>
    public string? HealthNotesProtected { get; set; }

    public LocalDate? LastPatchTestOn { get; set; }

    public bool MarketingConsent { get; set; }

    /// <summary>When consent was given; cleared when it is withdrawn.</summary>
    public Instant? MarketingConsentAt { get; set; }

    /// <summary>The exact text the client agreed to, so the salon can show what was consented to.</summary>
    public string? MarketingConsentWording { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// This client's own deposit percentage (ADR 0023): null follows the service and the salon, 0 never asks them for a
    /// deposit, anything above always does, whatever the service says.
    /// </summary>
    public decimal? DepositPercent { get; set; }

    /// <summary>
    /// Set when this card was merged into another (<see cref="ClientMergeRules"/>): the row stays, inactive and without contact
    /// details, as a pointer so old links and imports still resolve. Never set on an active client.
    /// </summary>
    public Guid? MergedIntoClientId { get; set; }

    public Instant? MergedAt { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }

    public string FullName => $"{FirstName} {LastName}";
}