using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;

namespace Salon.Web.Framework.Mvc;

/// <summary>
/// Lets a slice keep its views next to its controller: a controller in namespace
/// <c>Salon.Web.Features.Catalog.CreateService</c> resolves views from <c>/Features/Catalog/CreateService/</c>.
/// The host namespace is not assumed; everything after the <c>.Features.</c> segment becomes the folder path.
/// </summary>
public sealed class FeatureViewLocationExpander : IViewLocationExpander
{
    private const string FeatureKey = "feature";
    private const string FeaturesSegment = ".Features.";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ActionContext.ActionDescriptor is ControllerActionDescriptor descriptor
            && descriptor.ControllerTypeInfo.Namespace is { } ns
            && ns.IndexOf(FeaturesSegment, StringComparison.Ordinal) is var index and >= 0)
        {
            context.Values[FeatureKey] = ns[(index + FeaturesSegment.Length)..].Replace('.', '/');
        }
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(viewLocations);

        if (context.Values.TryGetValue(FeatureKey, out var feature))
        {
            viewLocations = viewLocations.Prepend("/Features/Shared/{0}.cshtml").Prepend($"/Features/{feature}/{{0}}.cshtml");
        }

        return viewLocations;
    }
}