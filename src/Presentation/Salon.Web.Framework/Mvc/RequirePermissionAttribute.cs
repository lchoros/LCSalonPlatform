using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Identity;

namespace Salon.Web.Framework.Mvc;

/// <summary>
/// Admits only a signed-in user whose role grants <see cref="Permission"/> (ADR 0009). Anonymous on a salon's host →
/// challenge (login redirect, or 401 for non-HTML); anonymous on the platform domain → 404, because no login exists
/// there; signed in without the permission → forbid (denied page, or 403). A cookie whose tenant is not the request's
/// tenant counts as anonymous even if an earlier middleware let it through. Composes with <see cref="RequireTenantAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    public RequirePermissionAttribute(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);
        if (!Permissions.All.Contains(permission, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{permission}' is not a known permission. Add it to {nameof(Permissions)} first.", nameof(permission));
        }

        Permission = permission;
    }

    /// <summary>One of <see cref="Permissions.All"/>, e.g. "Catalog.Manage".</summary>
    public string Permission { get; }

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Result is not null)
        {
            return Task.CompletedTask;
        }

        var tenantId = context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId;
        var user = context.HttpContext.User;
        var signedIn = user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirst(UserClaimTypes.TenantId)?.Value, out var claimedTenant)
            && claimedTenant == tenantId;

        if (!signedIn)
        {
            context.Result = tenantId is null ? new NotFoundResult() : new ChallengeResult(SalonAuthenticationDefaults.Scheme);
        }
        else if (!user.HasClaim(UserClaimTypes.Permission, Permission))
        {
            context.Result = new ForbidResult(SalonAuthenticationDefaults.Scheme);
        }

        return Task.CompletedTask;
    }
}