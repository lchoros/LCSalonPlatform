using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Identity;

namespace Salon.Web.Framework.Tenancy;

/// <summary>
/// Sets the current tenant for the request. On a salon's subdomain the slug decides; a signed-in user whose
/// <c>tenant_id</c> claim names another salon is treated as anonymous and the cookie is cleared (ADR 0009). On the bare
/// base domain there is no tenant, unless an authenticated user carries a claim (ADR 0002). Both paths verify the tenant
/// exists and is active; otherwise the request is a 404.
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
        var claimedTenantId = ClaimedTenantId(context.User, settings.TenantClaimType);
        TenantInfo? tenant = null;

        if (SubdomainTenantSlugParser.TryGetSlug(context.Request.Host, settings.BaseDomains, out var slug))
        {
            tenant = await lookup.FindBySlugAsync(slug, context.RequestAborted);
            if (tenant is null || !tenant.IsActive)
            {
                logger.LogDebug("No active tenant for slug {Slug}", slug);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (claimedTenantId is { } claimed && claimed != tenant.Id)
            {
                // The cookie is bound to another salon: ignore it for this request and tell the browser to drop it.
                logger.LogWarning("Signed-in user of tenant {ClaimedTenantId} sent a cookie to {Slug}; treating the request as anonymous", claimed, slug);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
                await context.SignOutAsync(SalonAuthenticationDefaults.Scheme);
            }
        }
        else if (claimedTenantId is { } claimed)
        {
            tenant = await lookup.FindByIdAsync(claimed, context.RequestAborted);
            if (tenant is null || !tenant.IsActive)
            {
                logger.LogWarning("Authenticated user carries tenant claim {TenantId} that is missing or inactive", claimed);
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

    private static Guid? ClaimedTenantId(ClaimsPrincipal? user, string claimType)
        => user?.Identity?.IsAuthenticated == true && Guid.TryParse(user.FindFirst(claimType)?.Value, out var tenantId)
            ? tenantId
            : null;
}

public static class TenantResolutionMiddlewareExtensions
{
    /// <summary>Place after UseAuthentication (so the claim is available) and before routing-dependent middleware.</summary>
    public static IApplicationBuilder UseSalonTenancy(this IApplicationBuilder app)
        => app.UseMiddleware<TenantResolutionMiddleware>();
}