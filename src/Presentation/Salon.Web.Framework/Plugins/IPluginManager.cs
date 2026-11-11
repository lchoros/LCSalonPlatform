using Salon.Core.Plugins;

namespace Salon.Web.Framework.Plugins;

/// <summary>Registry of the compiled-in plugins (payments, SMS, widgets, import, …) with their descriptors.</summary>
public interface IPluginManager
{
    IReadOnlyList<LoadedPlugin> Plugins { get; }

    /// <summary>Plugins implementing <typeparamref name="TPlugin"/>, in registration order.</summary>
    IReadOnlyList<TPlugin> GetPlugins<TPlugin>()
        where TPlugin : class, IPlugin;

    LoadedPlugin? FindBySystemName(string systemName);
}

public sealed record LoadedPlugin(IPlugin Instance, PluginDescriptor Descriptor);