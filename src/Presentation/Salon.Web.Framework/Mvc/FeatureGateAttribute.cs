using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Salon.Web.Framework.Infrastructure;

namespace Salon.Web.Framework.Mvc;

/// <summary>
/// Hides a controller or action behind a flag of <see cref="FeaturesOptions"/>: when the flag is off the request is a
/// plain 404, indistinguishable from a route that does not exist. The flag is read per request through
/// <see cref="IOptionsMonitor{TOptions}"/>, so a configuration reload takes effect without a restart.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class FeatureGateAttribute : Attribute, IActionFilter
{
    public FeatureGateAttribute(string flag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flag);
        Flag = flag;
        Property = typeof(FeaturesOptions).GetProperty(flag, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new ArgumentException($"'{flag}' is not a flag on {nameof(FeaturesOptions)}. Add a bool property there first.", nameof(flag));
        if (Property.PropertyType != typeof(bool))
        {
            throw new ArgumentException($"{nameof(FeaturesOptions)}.{flag} is not a bool.", nameof(flag));
        }
    }

    /// <summary>The <see cref="FeaturesOptions"/> property name, e.g. "CatalogAdmin".</summary>
    public string Flag { get; }

    private PropertyInfo Property { get; }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.HttpContext.RequestServices.GetRequiredService<IOptionsMonitor<FeaturesOptions>>().CurrentValue;
        if (!(bool)Property.GetValue(options)!)
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}