using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

using Salon.Core.Plugins;
using Salon.Web.Framework.Plugins;

namespace Salon.Web.Framework.TagHelpers;

/// <summary>
/// <c>&lt;widget-zone name="admin_dashboard_top" /&gt;</c> renders, in plugin order, the view component of every
/// <see cref="IWidgetPlugin"/> that claims the zone. Renders nothing (no wrapper) when no plugin does.
/// </summary>
[HtmlTargetElement("widget-zone", Attributes = "name", TagStructure = TagStructure.WithoutEndTag)]
public sealed class WidgetZoneTagHelper(IPluginManager pluginManager, IViewComponentHelper viewComponentHelper) : TagHelper
{
    public string Name { get; set; } = string.Empty;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = null;

        var widgets = pluginManager.GetPlugins<IWidgetPlugin>()
            .Where(w => w.WidgetZones.Contains(Name, StringComparer.Ordinal))
            .ToList();
        if (widgets.Count == 0)
        {
            output.SuppressOutput();
            return;
        }

        (viewComponentHelper as IViewContextAware)?.Contextualize(ViewContext);
        foreach (var widget in widgets)
        {
            var componentName = widget.GetWidgetViewComponentName(Name);
            output.Content.AppendHtml(await viewComponentHelper.InvokeAsync(componentName, new { widgetZone = Name }));
        }
    }
}