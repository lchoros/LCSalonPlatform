namespace Salon.Web.Framework.Plugins;

/// <summary>A plugin assembly is mis-packaged (no plugin.json, bad JSON, or a SystemName mismatch). UseSalonPlugins surfaces it at startup.</summary>
public class PluginLoadException : InvalidOperationException
{
    public PluginLoadException()
    {
    }

    public PluginLoadException(string message)
        : base(message)
    {
    }

    public PluginLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}