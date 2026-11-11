using Microsoft.Extensions.Logging;

using Salon.Core.Plugins;

namespace Salon.Web.Framework.Plugins;

/// <summary>Built once at startup from every <see cref="IPlugin"/> registered in DI. Duplicate SystemNames fail startup.</summary>
public sealed class PluginManager : IPluginManager
{
    private readonly Dictionary<string, LoadedPlugin> _bySystemName = new(StringComparer.Ordinal);

    public PluginManager(IEnumerable<IPlugin> plugins, ILogger<PluginManager> logger)
    {
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(logger);

        var loaded = new List<LoadedPlugin>();
        foreach (var plugin in plugins)
        {
            var descriptor = PluginDescriptorReader.Read(plugin);
            if (!_bySystemName.TryAdd(descriptor.SystemName, new LoadedPlugin(plugin, descriptor)))
            {
                throw new PluginLoadException($"Two plugins register the SystemName '{descriptor.SystemName}'.");
            }

            loaded.Add(_bySystemName[descriptor.SystemName]);
            logger.LogInformation("Plugin {SystemName} {Version} ({Group}) loaded", descriptor.SystemName, descriptor.Version, descriptor.Group);
        }

        Plugins = loaded;
    }

    public IReadOnlyList<LoadedPlugin> Plugins { get; }

    public IReadOnlyList<TPlugin> GetPlugins<TPlugin>()
        where TPlugin : class, IPlugin
        => Plugins.Select(p => p.Instance).OfType<TPlugin>().ToList();

    public LoadedPlugin? FindBySystemName(string systemName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        return _bySystemName.GetValueOrDefault(systemName);
    }
}