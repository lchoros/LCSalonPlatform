using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Salon.Core.Tenancy;
using Salon.Services.Tenancy;

namespace Salon.Web.Framework.Tenancy;

/// <summary>
/// Sets the current tenant for the request. Order: the tenant claim of an <em>authenticated</em> user (admin), then
/// the subdomain (public booking). Both paths verify the tenant exists and is active; otherwise the request is a 404.
/// The bare base domain has no tenant (platform pages).
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
        TenantInfo? tenant = null;

        if (context.User?.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst(settings.TenantClaimType)?.Value, out var claimedTenantId))
        {
            tenant = await lookup.FindByIdAsync(claimedTenantId, context.RequestAborted);
            if (tenant is null || !tenant.IsActive)
            {
                logger.LogWarning("Authenticated user carries tenant claim {TenantId} that is missing or inactive", claimedTenantId);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }
        else if (SubdomainTenantSlugParser.TryGetSlug(context.Request.Host, settings.BaseDomains, out var slug))
        {
            tenant = await lookup.FindBySlugAsync(slug, context.RequestAborted);
            if (tenant is null || !tenant.IsActive)
            {
                logger.LogDebug("No active tenant for slug {Slug}", slug);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }
        }

        if (tenant is not null)
        {
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