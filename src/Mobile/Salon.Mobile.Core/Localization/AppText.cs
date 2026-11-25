using System.Collections.Frozen;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace Salon.Mobile.Core.Localization;

/// <summary>
/// <see cref="IAppText"/> over two embedded dictionaries, Localization/en-GB.json and Localization/pl-PL.json. The
/// language follows the salon's culture: pl-* speaks Polish, everything else English; numbers and dates use the
/// culture itself.
/// </summary>
public sealed class AppText : IAppText
{
    public const string English = "en-GB";
    public const string Polish = "pl-PL";

    private const string ResourcePrefix = "Salon.Mobile.Core.Localization.";

    private static readonly FrozenDictionary<string, FrozenDictionary<string, string>> Dictionaries = LoadAll();

    private FrozenDictionary<string, string> _words = Dictionaries[English];

    public AppText(string? cultureName = null)
    {
        Culture = CultureInfo.GetCultureInfo(English);
        Language = English;
        UseCulture(cultureName ?? English);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The languages there are dictionaries for.</summary>
    public static IReadOnlyCollection<string> Languages => Dictionaries.Keys;

    public CultureInfo Culture { get; private set; }

    public string Language { get; private set; }

    public string this[string key]
    {
        get
        {
            if (_words.TryGetValue(key, out var words) || Dictionaries[English].TryGetValue(key, out words))
            {
                return words;
            }

            return key;
        }
    }

    /// <summary>One language's dictionary as embedded, for the parity tests.</summary>
    public static IReadOnlyDictionary<string, string> DictionaryFor(string language)
    {
        return Dictionaries[language];
    }

    public string Format(string key, params object?[] args)
    {
        return string.Format(Culture, this[key], args);
    }

    public void UseCulture(string? cultureName)
    {
        var culture = Resolve(cultureName);
        var language = string.Equals(culture.TwoLetterISOLanguageName, "pl", StringComparison.OrdinalIgnoreCase) ? Polish : English;
        if (Equals(culture, Culture) && string.Equals(language, Language, StringComparison.Ordinal))
        {
            return;
        }

        Culture = culture;
        Language = language;
        _words = Dictionaries[language];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Culture)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    private static CultureInfo Resolve(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
        {
            return CultureInfo.GetCultureInfo(English);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);

            // A neutral culture ("pl") has no region, so no currency of its own; take the one the app knows.
            if (culture.IsNeutralCulture || string.IsNullOrEmpty(culture.Name))
            {
                return CultureInfo.GetCultureInfo(string.Equals(culture.TwoLetterISOLanguageName, "pl", StringComparison.OrdinalIgnoreCase) ? Polish : English);
            }

            return culture;
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo(English);
        }
    }

    private static FrozenDictionary<string, FrozenDictionary<string, string>> LoadAll()
    {
        var all = new Dictionary<string, FrozenDictionary<string, string>>(StringComparer.Ordinal)
        {
            [English] = Load(English),
            [Polish] = Load(Polish),
        };
        return all.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, string> Load(string language)
    {
        var name = ResourcePrefix + language + ".json";
        using var stream = typeof(AppText).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The app's words for {language} are not embedded ({name}).");
        using var document = JsonDocument.Parse(stream);

        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!words.TryAdd(property.Name, property.Value.GetString() ?? string.Empty))
            {
                throw new InvalidOperationException($"The key {property.Name} is in {name} twice.");
            }
        }

        return words.ToFrozenDictionary(StringComparer.Ordinal);
    }
}