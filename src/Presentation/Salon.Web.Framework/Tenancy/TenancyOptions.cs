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

    /// <summary>Claim carrying the tenant id for signed-in admin users. Wins over the subdomain.</summary>
    public string TenantClaimType { get; set; } = "tenant_id";
}