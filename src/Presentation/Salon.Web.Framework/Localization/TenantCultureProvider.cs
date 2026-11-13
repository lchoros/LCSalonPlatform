using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Localization;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;

namespace Salon.Web.Framework.Localization;

/// <summary>
/// Picks the salon's own culture (<c>Tenant.Culture</c>) for requests that resolved a tenant. Needs the tenant context to
/// be set already, so <c>UseSalonLocalization()</c> must run after <c>UseSalonTenancy()</c>. Platform pages (no tenant)
/// and tenants with an unsupported culture yield nothing and let the next provider decide.
/// </summary>
public sealed class TenantCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var tenantContext = httpContext.RequestServices.GetRequiredService<ITenantContext>();
        if (tenantContext.TenantId is not { } tenantId)
        {
            return null;
        }

        var tenant = await httpContext.RequestServices.GetRequiredService<ITenantLookup>()
            .FindByIdAsync(tenantId, httpContext.RequestAborted).ConfigureAwait(false);

        return tenant is not null && SupportedCultures.IsSupported(tenant.Culture)
            ? new ProviderCultureResult(tenant.Culture)
            : null;
    }
}