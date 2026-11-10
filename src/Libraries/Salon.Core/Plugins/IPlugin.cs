namespace Salon.Core.Plugins;

/// <summary>
/// A compile-time plugin (payments, SMS, import, calendar sync). Discovered by the plugin loader in Salon.Web.Framework.
/// </summary>
public interface IPlugin
{
    /// <summary>Stable machine name, e.g. "Payments.Manual". Stored in settings and audit rows.</summary>
    string SystemName { get; }

    /// <summary>Human name shown in admin. Resource key, localised through ILocalizationService.</summary>
    string DisplayNameKey { get; }
}