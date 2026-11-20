using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Identity;

namespace Salon.Web.Framework.Mvc;

/// <summary>
/// Admits any signed-in user of the request's tenant, whatever their role — for pages about the person's own login
/// (ADR 0077). Anonymous on a salon's host → challenge (login redirect, or 401 for non-HTML); anonymous on the
/// platform domain → 404, because no login exists there. A cookie whose tenant is not the request's tenant counts
/// as anonymous. The permission-checking sibling is <see cref="RequirePermissionAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireSignedInAttribute : Attribute, IAsyncAuthorizationFilter
{
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

        return Task.CompletedTask;
    }
}