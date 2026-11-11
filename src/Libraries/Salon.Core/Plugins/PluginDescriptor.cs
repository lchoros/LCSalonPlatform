namespace Salon.Core.Plugins;

/// <summary>
/// Metadata read from the <c>plugin.json</c> embedded in every plugin assembly. The loader refuses a plugin whose
/// descriptor is missing or whose SystemName differs from the <see cref="IPlugin.SystemName"/> of the class.
/// </summary>
public sealed record PluginDescriptor(
    string SystemName,
    string DisplayNameKey,
    string Group,
    string Author,
    string Version,
    string Description);