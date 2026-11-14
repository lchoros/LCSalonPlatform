using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Tenancy;

namespace Salon.Web.Framework.Mvc;

/// <summary>
/// Marks a controller or action that only makes sense for one salon: on the bare platform domain, where
/// <see cref="ITenantContext.TenantId"/> is null, the request is a 404 before any action code or model binding
/// validation runs. The pipeline's tenant guard (ADR 0003) still backs this for every request that gets through.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireTenantAttribute : Attribute, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.HttpContext.RequestServices.GetRequiredService<ITenantContext>().TenantId is null)
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}