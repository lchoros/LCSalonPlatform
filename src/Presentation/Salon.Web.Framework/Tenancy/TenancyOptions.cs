using Salon.Core.Identity;

namespace Salon.Web.Framework.Tenancy;

/// <summary>Host-level tenancy settings (section "Tenancy"). Per-tenant settings live in ISettingService, not here.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    public const string DefaultBaseDomain = "localhost";

    /// <summary>
    /// Domains under which "{slug}.{domain}" selects a tenant. The bare domain is the platform site.
    /// Empty configuration falls back to <see cref="DefaultBaseDomain"/> (see AddSalonFramework); configured values replace it.
    /// </summary>
    public IList<string> BaseDomains { get; set; } = [];

    /// <summary>
    /// Claim carrying the tenant id for signed-in admin users; the sign-in cookie writes <see cref="UserClaimTypes.TenantId"/>,
    /// so only change this together with a custom claims factory.
    /// </summary>
    public string TenantClaimType { get; set; } = UserClaimTypes.TenantId;

    /// <summary>
    /// Scheme of links in SMS and email (ADR 0014): <c>https</c> unless set. Null means "https, or http in Development";
    /// production never sets it to http.
    /// </summary>
    public string? PublicScheme { get; set; }

    /// <summary>Port of those links when the host is not on the scheme's default port (a local Kestrel on 5227); null omits it.</summary>
    public int? PublicPort { get; set; }
}