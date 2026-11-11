using System.Diagnostics.CodeAnalysis;

namespace Salon.Services.Localization;

/// <summary>
/// UI text by resource key (CLAUDE.md rule 6). Lookup falls back requested culture → <see cref="Salon.Core.Localization.SupportedCultures.Default"/>
/// → the key itself, so a missing translation never breaks a page; it is logged once instead. Views use the injected
/// <c>T</c>: <c>@T["Home.Title"]</c>.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// The culture names resources were loaded for. Reading it loads and validates every source, so the host touches it at
    /// startup to fail fast on packaging mistakes.
    /// </summary>
    IReadOnlyCollection<string> Cultures { get; }

    /// <summary>Text for <paramref name="key"/> in <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.</summary>
    string this[string key] => Get(key);

    /// <summary>Text for <paramref name="key"/> in <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>.</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "'Get' is the agreed API (ADR 0006); the solution is C# only.")]
    string Get(string key);

    /// <summary>Text for <paramref name="key"/> in the given culture, e.g. for an SMS to a client whose salon speaks Polish.</summary>
    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "'Get' is the agreed API (ADR 0006); the solution is C# only.")]
    string Get(string key, string culture);

    /// <summary><see cref="Get(string)"/> then <see cref="string.Format(IFormatProvider, string, object[])"/> with the current culture.</summary>
    string Format(string key, params object?[] args);

    /// <summary>True when the culture itself (no fallback) defines the key.</summary>
    bool Exists(string key, string culture);
}