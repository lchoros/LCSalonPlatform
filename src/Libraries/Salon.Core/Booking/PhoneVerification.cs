using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Booking;

/// <summary>
/// One SMS code sent to a client who is booking online (ADR 0014). Tenant-owned. The row carries the whole pending booking
/// (service, staff or "anyone", start, names, contact, consent) so the verify step needs no session state: the verify
/// page is addressed by <see cref="BaseEntity.Id"/> alone. The code itself is never stored; <see cref="CodeHash"/> is
/// SHA-256 of <c>"{salt}:{code}"</c> with a random per-row <see cref="CodeSalt"/>. A row dies after
/// <see cref="PhoneVerificationRules.MaxAttempts"/> wrong codes or at <see cref="ExpiresAt"/>, and is spent once
/// <see cref="VerifiedAt"/> is set. Only <see cref="PhoneVerificationRules"/> changes <see cref="Attempts"/> and <see cref="VerifiedAt"/>.
/// </summary>
public class PhoneVerification : BaseEntity, ITenantEntity
{
    public const int PhoneMaxLength = 20;
    public const int HashMaxLength = 64;
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int ConsentWordingMaxLength = 500;

    public Guid TenantId { get; set; }

    /// <summary>Normalised recipient, e.g. <c>+447700900123</c>; also the client lookup key once verified.</summary>
    public required string PhoneE164 { get; set; }

    /// <summary>Base64 of 16 random bytes, one per row.</summary>
    public required string CodeSalt { get; set; }

    /// <summary>Base64 SHA-256 of <c>"{CodeSalt}:{code}"</c>.</summary>
    public required string CodeHash { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant ExpiresAt { get; set; }

    /// <summary>Wrong codes entered so far; <see cref="PhoneVerificationRules.MaxAttempts"/> kills the row.</summary>
    public int Attempts { get; set; }

    public Instant? VerifiedAt { get; set; }

    /// <summary>Set once the verified booking has been made, so a repeated POST lands on the same confirmation.</summary>
    public Guid? AppointmentId { get; set; }

    // The pending booking.
    public Guid ServiceId { get; set; }

    /// <summary>Null means "anyone available": the staff member is chosen when the booking is made.</summary>
    public Guid? StaffMemberId { get; set; }

    public Instant Start { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public string? Email { get; set; }

    public bool MarketingConsent { get; set; }

    /// <summary>The consent text shown next to the tick, recorded on the client card when the booking is made.</summary>
    public string? ConsentWording { get; set; }
}