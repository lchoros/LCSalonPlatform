using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Salon.Web.Framework.Plugins;

public static class PluginServiceCollectionExtensions
{
    /// <summary>
    /// Registers the plugin manager. Each plugin registers itself (and its <c>IPlugin</c> binding) through its own
    /// <c>Add…</c> extension before this call. Pair with <see cref="UseSalonPlugins"/> so bad packaging fails at startup.
    /// </summary>
    public static IServiceCollection AddSalonPlugins(this IServiceCollection services)
    {
        services.AddSingleton<IPluginManager, PluginManager>();
        return services;
    }

    /// <summary>Builds the plugin manager now, so a missing or mismatched plugin.json stops the host instead of the first page render.</summary>
    public static IApplicationBuilder UseSalonPlugins(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.ApplicationServices.GetRequiredService<IPluginManager>();
        return app;
    }
}