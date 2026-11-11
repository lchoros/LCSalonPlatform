namespace Salon.Services.Localization;

/// <summary>
/// One origin of UI strings (the host, a plugin). <see cref="LocalizationService"/> merges all registered sources once;
/// two sources must never define the same key for the same culture.
/// </summary>
public interface IResourceSource
{
    /// <summary>Shown in load errors and logs, e.g. the assembly name.</summary>
    string Name { get; }

    /// <summary>Culture name → (resource key → text). Culture names are compared ignoring case.</summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load();
}