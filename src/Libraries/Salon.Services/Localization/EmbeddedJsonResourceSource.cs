using System.Reflection;
using System.Text.Json;

using Salon.Core.Localization;

namespace Salon.Services.Localization;

/// <summary>
/// Reads the flat JSON resource files an assembly embeds under <c>{resourceRoot}/{culture}.json</c>, i.e. manifest
/// names ending in <c>.{resourceRoot}.{culture}.json</c> such as <c>Salon.Web.Localization.en-GB.json</c>. Each file is
/// one object of <c>"Area.Key": "text"</c>. Packaging mistakes fail the load: a culture outside
/// <see cref="SupportedCultures.All"/>, two resources for one culture, a key repeated inside one file, or invalid JSON.
/// </summary>
public sealed class EmbeddedJsonResourceSource : IResourceSource
{
    private const string Extension = ".json";

    private readonly Assembly _assembly;
    private readonly string _marker;

    public EmbeddedJsonResourceSource(Assembly assembly, string resourceRoot = "Localization")
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        _assembly = assembly;
        _marker = $".{resourceRoot}.";
        Name = $"{assembly.GetName().Name} ({resourceRoot})";
    }

    public string Name { get; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var resourceByCulture = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Sorted so that an error names the same pair of resources on every run.
        foreach (var resourceName in _assembly.GetManifestResourceNames().Order(StringComparer.Ordinal))
        {
            if (!resourceName.EndsWith(Extension, StringComparison.Ordinal))
            {
                continue;
            }

            var markerAt = resourceName.LastIndexOf(_marker, StringComparison.Ordinal);
            if (markerAt < 0)
            {
                continue;
            }

            var culture = resourceName[(markerAt + _marker.Length)..^Extension.Length];
            if (!SupportedCultures.IsSupported(culture))
            {
                throw new InvalidOperationException(
                    $"{Name}: embedded resource '{resourceName}' is for culture '{culture}', which is not one of: {string.Join(", ", SupportedCultures.All)}.");
            }

            if (!resourceByCulture.TryAdd(culture, resourceName))
            {
                throw new InvalidOperationException(
                    $"{Name}: embedded resources '{resourceByCulture[culture]}' and '{resourceName}' are both for culture '{culture}'; an assembly ships one file per culture.");
            }

            result[culture] = Read(resourceName);
        }

        return result;
    }

    private Dictionary<string, string> Read(string resourceName)
    {
        using var stream = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"{Name}: embedded resource '{resourceName}' could not be opened.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{Name}: embedded resource '{resourceName}' is not valid JSON.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"{Name}: embedded resource '{resourceName}' must be a flat JSON object of key → text.");
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        $"{Name}: embedded resource '{resourceName}' key '{property.Name}' must be a string, not {property.Value.ValueKind}.");
                }

                if (!entries.TryAdd(property.Name, property.Value.GetString()!))
                {
                    throw new InvalidOperationException($"{Name}: embedded resource '{resourceName}' defines key '{property.Name}' more than once.");
                }
            }

            return entries;
        }
    }
}