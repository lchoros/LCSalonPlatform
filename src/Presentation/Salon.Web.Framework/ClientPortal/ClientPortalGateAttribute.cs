using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Salon.Core.Tenancy;
using Salon.Web.Framework.Infrastructure;

namespace Salon.Web.Framework.ClientPortal;

/// <summary>
/// The door of a client's own pages (ADR 0083): while <see cref="FeaturesOptions.ClientPortal"/> is off, or on the
/// bare platform domain where there is no salon, every address behind it is a plain 404. An authorization filter, not
/// an action filter like <c>[FeatureGate]</c>: it runs before the anti-forgery check, so a POST to a switched-off page
/// is a 404 too and not a 400 that would give the page away.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ClientPortalGateAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var services = context.HttpContext.RequestServices;
        if (!services.GetRequiredService<IOptionsMonitor<FeaturesOptions>>().CurrentValue.ClientPortal
            || services.GetRequiredService<ITenantContext>().TenantId is null)
        {
            context.Result = new NotFoundResult();
        }
    }
}