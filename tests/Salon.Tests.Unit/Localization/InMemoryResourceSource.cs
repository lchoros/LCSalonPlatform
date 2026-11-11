using Salon.Services.Localization;

namespace Salon.Tests.Unit.Localization;

/// <summary>A resource source built from literals: culture → (key → value).</summary>
internal sealed class InMemoryResourceSource(string name, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources) : IResourceSource
{
    public string Name => name;

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load() => resources;

    public static InMemoryResourceSource Of(string name, params (string Culture, string Key, string Value)[] entries)
    {
        var byCulture = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in entries.GroupBy(e => e.Culture, StringComparer.OrdinalIgnoreCase))
        {
            byCulture[group.Key] = group.ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);
        }

        return new InMemoryResourceSource(name, byCulture);
    }
}