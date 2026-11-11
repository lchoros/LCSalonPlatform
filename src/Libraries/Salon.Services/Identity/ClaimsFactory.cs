using System.Security.Claims;

using Salon.Core.Identity;

namespace Salon.Services.Identity;

/// <summary>Builds the principal the sign-in cookie carries (ADR 0009): identity, tenant, role, one claim per permission, stamp.</summary>
public static class ClaimsFactory
{
    /// <summary>Authentication type of the identity; also the name of the cookie scheme in Salon.Web.Framework.</summary>
    public const string AuthenticationType = "salon.auth";

    public static ClaimsPrincipal Create(User user, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.TenantId != tenantId)
        {
            throw new ArgumentException($"User {user.Id} belongs to tenant {user.TenantId}, not {tenantId}.", nameof(tenantId));
        }

        var claims = new List<Claim>
        {
            new(UserClaimTypes.Subject, user.Id.ToString()),
            new(UserClaimTypes.TenantId, tenantId.ToString()),
            new(UserClaimTypes.Name, user.DisplayName),
            new(UserClaimTypes.Role, user.Role.ToString()),
            new(UserClaimTypes.SecurityStamp, user.SecurityStamp),
        };
        claims.AddRange(RolePermissions.For(user.Role).Select(p => new Claim(UserClaimTypes.Permission, p)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationType, UserClaimTypes.Name, UserClaimTypes.Role));
    }
}