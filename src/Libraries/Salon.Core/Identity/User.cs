using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Identity;

/// <summary>
/// A salon's staff login (ADR 0009). Tenant-owned: the same email may exist in two salons. Never holds a plain password;
/// <see cref="PasswordHash"/> is produced and checked by the password service. <see cref="SecurityStamp"/> changes
/// whenever the password, role or active state changes, which signs the user out everywhere.
/// </summary>
public class User : BaseEntity, ITenantEntity
{
    public const int EmailMaxLength = 254;
    public const int DisplayNameMaxLength = 100;

    public Guid TenantId { get; set; }

    /// <summary>Stored lower-case; unique per tenant.</summary>
    public required string Email { get; set; }

    public required string DisplayName { get; set; }

    public required string PasswordHash { get; set; }

    public Role Role { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Consecutive failed sign-ins since the last success or lockout; see <see cref="LockoutPolicy"/>.</summary>
    public int FailedLoginCount { get; set; }

    /// <summary>Set when the account is locked; a sign-in before this instant fails without checking the password.</summary>
    public Instant? LockedOutUntil { get; set; }

    /// <summary>Random value (a GUID string) rotated on security-relevant changes.</summary>
    public required string SecurityStamp { get; set; }

    public Instant CreatedAt { get; set; }

    public Instant? LastLoginAt { get; set; }

    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");
}