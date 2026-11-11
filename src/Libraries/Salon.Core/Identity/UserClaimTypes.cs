namespace Salon.Core.Identity;

/// <summary>Claim types in the sign-in cookie (ADR 0009). Short names: the cookie carries them on every request.</summary>
public static class UserClaimTypes
{
    /// <summary>The user id.</summary>
    public const string Subject = "sub";

    /// <summary>The user's tenant; the tenant middleware reads this claim, so it must match <c>TenancyOptions.TenantClaimType</c>.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>Display name, shown in the layout.</summary>
    public const string Name = "name";

    /// <summary>The <see cref="Role"/> name.</summary>
    public const string Role = "role";

    /// <summary>One claim per permission of the role; see <see cref="Permissions"/>.</summary>
    public const string Permission = "permission";

    /// <summary>Copy of <see cref="User.SecurityStamp"/>; a mismatch on validation ends the session.</summary>
    public const string SecurityStamp = "security_stamp";
}