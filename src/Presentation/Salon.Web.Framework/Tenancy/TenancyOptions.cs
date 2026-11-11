namespace Salon.Web.Framework.Tenancy;

/// <summary>Host-level tenancy settings (section "Tenancy"). Per-tenant settings live in ISettingService, not here.</summary>
public sealed class TenancyOptions
{
    public const string SectionName = "Tenancy";

    /// <summary>Domains under which "{slug}.{domain}" selects a tenant. The bare domain is the platform site.</summary>
    public IList<string> BaseDomains { get; } = ["localhost"];

    /// <summary>Claim carrying the tenant id for signed-in admin users. Wins over the subdomain.</summary>
    public string TenantClaimType { get; set; } = "tenant_id";
}