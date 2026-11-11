using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Salon.Core.Tenancy;
using Salon.Services.Tenancy;

namespace Salon.Web.Framework.Tenancy;

/// <summary>
/// Sets the current tenant for the request. Order: the signed-in user's tenant claim (admin), then the subdomain
/// (public booking). An unknown or inactive subdomain is a 404. The bare base domain has no tenant (platform pages).
/// </summary>
public sealed class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<TenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IMutableTenantContext tenantContext, ITenantLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(lookup);

        var settings = options.Value;

        var claim = context.User?.FindFirst(settings.TenantClaimType)?.Value;
        if (claim is not null && Guid.TryParse(claim, out var claimedTenantId))
        {
            tenantContext.SetTenant(claimedTenantId);
            await next(context);
            return;
        }

        if (SubdomainTenantSlugParser.TryGetSlug(context.Request.Host, settings.BaseDomains, out var slug))
        {
            var tenant = await lookup.FindBySlugAsync(slug, context.RequestAborted);
            if (tenant is null || !tenant.IsActive)
            {
                logger.LogInformation("No active tenant for host {Host}", context.Request.Host.Value);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            tenantContext.SetTenant(tenant.Id);
        }

        await next(context);
    }
}

public static class TenantResolutionMiddlewareExtensions
{
    /// <summary>Place after UseAuthentication (so the claim is available) and before routing-dependent middleware.</summary>
    public static IApplicationBuilder UseSalonTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}