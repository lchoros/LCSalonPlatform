using Microsoft.Extensions.DependencyInjection;

namespace Salon.Web.Framework.Plugins;

public static class PluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers the plugin manager. Each plugin registers itself (and its <c>IPlugin</c> binding) through its own
    /// <c>Add…</c> extension before this call; the manager reads every descriptor on first use and fails fast on bad packaging.
    /// </summary>
    public static IServiceCollection AddSalonPlugins(this IServiceCollection services)
    {
        services.AddSingleton<IPluginManager, PluginManager>();
        return services;
    }
}