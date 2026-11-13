using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Salon.Core.Localization;

namespace Salon.Services.Localization;

/// <summary>
/// Merges every <see cref="IResourceSource"/> once into one frozen table per culture. The table is built lazily so unit
/// tests can construct the service freely; the host forces it at startup through <see cref="Cultures"/> so packaging
/// mistakes stop the app instead of failing requests. A key supplied by two sources for the same culture fails the
/// load: plugins add text, they never override the host's (per-tenant overrides are a later, database-backed layer).
/// Each missing (culture, key) pair is logged once per process.
/// </summary>
internal sealed class LocalizationService : ILocalizationService
{
    private readonly IResourceSource[] _sources;
    private readonly ILogger<LocalizationService> _logger;
    private readonly Lazy<FrozenDictionary<string, FrozenDictionary<string, string>>> _table;

    // Bounded: cultures come from the request pipeline and keys from our own views, never from user input.
    private readonly ConcurrentDictionary<(string Culture, string Key), bool> _warned = new();

    public LocalizationService(IEnumerable<IResourceSource> sources, ILogger<LocalizationService> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(logger);
        _sources = sources.ToArray();
        _logger = logger;
        _table = new Lazy<FrozenDictionary<string, FrozenDictionary<string, string>>>(LoadAll, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyCollection<string> Cultures => _table.Value.Keys;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        // A neutral UI culture ("pl" on a job thread) still reads the matching supported culture.
        var current = CultureInfo.CurrentUICulture.Name;
        return Get(key, SupportedCultures.Match(current) ?? current);
    }

    public string Get(string key, string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);

        if (TryGet(culture, key, out var value))
        {
            return value;
        }

        // A supported culture without the key is a translation gap worth a warning; an unsupported one (a job thread on an
        // en-US server, the invariant culture) is expected to read the default silently.
        var isTranslationGap = SupportedCultures.IsSupported(culture) && !IsDefault(culture);
        if (TryGet(SupportedCultures.Default, key, out value))
        {
            if (isTranslationGap)
            {
                WarnOnce(culture, key, SupportedCultures.Default);
            }

            return value;
        }

        WarnOnce(culture, key, "the key itself");
        return key;
    }

    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    public bool Exists(string key, string culture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(culture);
        return TryGet(culture, key, out _);
    }

    private static bool IsDefault(string culture) => string.Equals(culture, SupportedCultures.Default, StringComparison.OrdinalIgnoreCase);

    private bool TryGet(string culture, string key, out string value)
    {
        value = string.Empty;
        return _table.Value.TryGetValue(culture, out var entries) && entries.TryGetValue(key, out value!);
    }

    private void WarnOnce(string culture, string key, string fallback)
    {
        if (_warned.TryAdd((culture, key), true))
        {
            _logger.LogWarning("No localization text for key {Key} in culture {Culture}; using {Fallback}", key, culture, fallback);
        }
    }

    private FrozenDictionary<string, FrozenDictionary<string, string>> LoadAll()
    {
        var merged = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<(string Culture, string Key), string>();

        foreach (var source in _sources)
        {
            foreach (var (culture, entries) in source.Load())
            {
                var normalizedCulture = SupportedCultures.Match(culture)
                    ?? throw new InvalidOperationException(
                        $"Localization source '{source.Name}' provides culture '{culture}', which is not one of: {string.Join(", ", SupportedCultures.All)}.");

                if (!merged.TryGetValue(normalizedCulture, out var target))
                {
                    target = new Dictionary<string, string>(StringComparer.Ordinal);
                    merged[normalizedCulture] = target;
                }

                foreach (var (key, value) in entries)
                {
                    if (owners.TryGetValue((normalizedCulture, key), out var owner))
                    {
                        throw new InvalidOperationException(
                            $"Localization key '{key}' for culture {normalizedCulture} is defined by both '{owner}' and '{source.Name}'. Sources add text; they do not override each other.");
                    }

                    owners[(normalizedCulture, key)] = source.Name;
                    target[key] = value;
                }
            }
        }

        var table = merged.ToFrozenDictionary(
            pair => pair.Key,
            pair => pair.Value.ToFrozenDictionary(StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation(
            "Loaded {KeyCount} localization strings for {Cultures} from {SourceCount} sources",
            table.Values.Sum(v => v.Count),
            string.Join(", ", table.Keys.Order(StringComparer.Ordinal)),
            _sources.Length);

        return table;
    }
}