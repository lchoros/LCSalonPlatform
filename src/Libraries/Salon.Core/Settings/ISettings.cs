namespace Salon.Core.Settings;

/// <summary>
/// Marker for a typed settings class. Each public writable property is stored as one row named
/// "{ClassName}.{PropertyName}" for the current tenant; the property initialiser is the default when no row exists.
/// Implementations need a public parameterless constructor.
/// </summary>
public interface ISettings
{
}