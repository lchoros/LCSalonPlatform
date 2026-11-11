namespace Salon.Core.Plugins;

/// <summary>
/// A plugin that renders into one or more named widget zones (see <see cref="WidgetZones"/>). The host renders the
/// view component named by <see cref="GetWidgetViewComponentName"/> inside <c>&lt;widget-zone name="…"&gt;</c>.
/// </summary>
public interface IWidgetPlugin : IPlugin
{
    IReadOnlyCollection<string> WidgetZones { get; }

    string GetWidgetViewComponentName(string widgetZone);
}