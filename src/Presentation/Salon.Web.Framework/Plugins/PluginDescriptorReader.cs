using System.Reflection;
using System.Text.Json;
using Salon.Core.Plugins;

namespace Salon.Web.Framework.Plugins;

/// <summary>Reads the embedded <c>plugin.json</c> of the assembly that defines a plugin type.</summary>
public static class PluginDescriptorReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static PluginDescriptor Read(IPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var descriptor = Read(plugin.GetType().Assembly);
        Validate(plugin, descriptor);
        return descriptor;
    }

    /// <summary>The class and its descriptor must agree on the SystemName; otherwise settings and audit rows would drift.</summary>
    public static void Validate(IPlugin plugin, PluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.SystemName, plugin.SystemName, StringComparison.Ordinal))
        {
            var type = plugin.GetType();
            throw new PluginLoadException(
                $"plugin.json in {type.Assembly.GetName().Name} declares SystemName '{descriptor.SystemName}' but {type.Name} reports '{plugin.SystemName}'.");
        }
    }

    public static PluginDescriptor Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("plugin.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new PluginLoadException($"{assembly.GetName().Name} has no embedded plugin.json.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        PluginDescriptor? descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize<PluginDescriptor>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PluginLoadException($"{assembly.GetName().Name}: plugin.json is not valid.", ex);
        }

        if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.SystemName) || string.IsNullOrWhiteSpace(descriptor.Version))
        {
            throw new PluginLoadException($"{assembly.GetName().Name}: plugin.json must set systemName and version.");
        }

        return descriptor;
    }
}