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

    /// <summary>
    /// The service being booked or waited for. Null only on a row that proves a number and carries nothing else
    /// (<see cref="PhoneVerificationPurpose.ClientPortal"/>, ADR 0083); the database refuses null for the other purposes.
    /// </summary>
    public Guid? ServiceId { get; set; }

    /// <summary>Null means "anyone available": the staff member is chosen when the booking is made.</summary>
    public Guid? StaffMemberId { get; set; }

    public Instant Start { get; set; }

    public required string FirstName { get; set; }

    public required string LastName { get; set; }

    public string? Email { get; set; }

    public bool MarketingConsent { get; set; }

    /// <summary>The consent text shown next to the tick, recorded on the client card when the booking is made.</summary>
    public string? ConsentWording { get; set; }

    /// <summary>
    /// What the verified number is for (ADR 0055). A row made for one purpose is never accepted by the other: a code
    /// sent for the waitlist cannot make a booking.
    /// </summary>
    public PhoneVerificationPurpose Purpose { get; set; } = PhoneVerificationPurpose.Booking;

    // The pending waitlist request, when that is the purpose; <see cref="Start"/> is then the moment it was asked.

    /// <summary>No use offering a day before this one; null means as soon as possible.</summary>
    public LocalDate? WaitNotBefore { get; set; }

    /// <summary>No use offering a day after this one; null means any time.</summary>
    public LocalDate? WaitNotAfter { get; set; }

    /// <summary>What the client wants the desk to know, such as "mornings only".</summary>
    public string? WaitNote { get; set; }
}
public enum PhoneVerificationPurpose
{
    /// <summary>The row carries a booking (ADR 0014).</summary>
    Booking = 1,

    /// <summary>The row carries a request to be told when a time comes up (ADR 0055).</summary>
    Waitlist = 2,

    /// <summary>
    /// The row carries nothing: it only proves the number, so a client can see their own upcoming visits (ADR 0083).
    /// No service and no names; <see cref="PhoneVerification.Start"/> is the moment it was asked.
    /// </summary>
    ClientPortal = 3,
}