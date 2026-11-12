using System.Reflection;
using System.Text.Json;

using Salon.Core.Localization;

namespace Salon.Services.Localization;

/// <summary>
/// Reads the flat JSON resource files an assembly embeds under its localization root (ADR 0006). Two layouts are
/// accepted and merged per culture:
/// <list type="bullet">
/// <item><c>{resourceRoot}/{culture}/{Module}.json</c>, manifest name <c>…{resourceRoot}.{culture}.{Module}.json</c>
/// (e.g. <c>Salon.Web.Localization.en-GB.Catalog.json</c>) — one file per module and culture, so a slice touches only
/// its module's files;</item>
/// <item><c>{resourceRoot}/{culture}.json</c>, manifest name <c>…{resourceRoot}.{culture}.json</c> — the legacy single
/// file, still used by assemblies with little text.</item>
/// </list>
/// Each file is one object of <c>"Area.Key": "text"</c>. Packaging mistakes fail the load: a culture outside
/// <see cref="SupportedCultures.All"/>, a key repeated inside one file, a key defined in two files of the same culture,
/// or invalid JSON.
/// </summary>
public sealed class EmbeddedJsonResourceSource : IResourceSource
{
    private const string Extension = ".json";

    private readonly Assembly _assembly;
    private readonly string _resourceRoot;
    private readonly string _marker;

    public EmbeddedJsonResourceSource(Assembly assembly, string resourceRoot = "Localization")
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceRoot);
        _assembly = assembly;
        _resourceRoot = resourceRoot;
        _marker = $".{resourceRoot}.";
        Name = $"{assembly.GetName().Name} ({resourceRoot})";
    }

    public string Name { get; }

    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Load()
    {
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<(string Culture, string Key), string>();

        foreach (var file in ReadFiles())
        {
            // ReadFiles already rejected unsupported cultures, so Match never returns null here.
            var culture = SupportedCultures.Match(file.Culture)!;
            if (!merged.TryGetValue(culture, out var target))
            {
                target = new Dictionary<string, string>(StringComparer.Ordinal);
                merged[culture] = target;
            }

            foreach (var (key, value) in file.Entries)
            {
                if (owners.TryGetValue((culture, key), out var owner))
                {
                    throw new InvalidOperationException(
                        $"{Name}: key '{key}' for culture '{culture}' is defined in both '{owner}' and '{file.ResourceName}'; a key lives in exactly one file per culture.");
                }

                owners[(culture, key)] = file.ResourceName;
                target[key] = value;
            }
        }

        return merged.ToDictionary(
            pair => pair.Key,
            IReadOnlyDictionary<string, string> (pair) => pair.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every resource under the root, parsed but not merged, in manifest-name order. Lets a test check parity file by file
    /// (a module present for one culture only) rather than key by key. Rejects unsupported cultures and invalid files.
    /// </summary>
    public IReadOnlyList<LocalizationResourceFile> ReadFiles()
    {
        var files = new List<LocalizationResourceFile>();

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

            // "en-GB" (legacy flat file) or "en-GB.Catalog" (module file under the culture folder).
            var path = resourceName[(markerAt + _marker.Length)..^Extension.Length];
            var dot = path.IndexOf('.', StringComparison.Ordinal);
            var culture = dot < 0 ? path : path[..dot];
            var module = dot < 0 ? null : path[(dot + 1)..];

            if (!SupportedCultures.IsSupported(culture))
            {
                throw new InvalidOperationException(
                    $"{Name}: embedded resource '{resourceName}' is for culture '{culture}', which is not one of: {string.Join(", ", SupportedCultures.All)}. Expected {_resourceRoot}/<culture>/<Module>.json or {_resourceRoot}/<culture>.json.");
            }

            if (module is { Length: 0 })
            {
                throw new InvalidOperationException($"{Name}: embedded resource '{resourceName}' has an empty module name.");
            }

            files.Add(new LocalizationResourceFile(resourceName, culture, module, Read(resourceName)));
        }

        return files;
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