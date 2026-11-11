namespace Salon.Core.Localization;

/// <summary>
/// The UI cultures the platform ships text for (CLAUDE.md rule 6). Tenants pick one of these as their default;
/// every resource file must exist for each of them.
/// </summary>
public static class SupportedCultures
{
    public const string EnGb = "en-GB";
    public const string PlPl = "pl-PL";

    /// <summary>The culture used when nothing else applies and the last fallback for missing translations.</summary>
    public const string Default = EnGb;

    public static IReadOnlyList<string> All { get; } = [EnGb, PlPl];

    /// <summary>True for exactly one of <see cref="All"/>, ignoring case. Neutral names such as "pl" are not supported cultures.</summary>
    public static bool IsSupported(string? name) => name is not null && All.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Maps a culture name onto the supported culture it denotes: an exact (case-insensitive) match, or the single supported
    /// culture whose language matches a neutral name ("pl" → "pl-PL", as browsers often send). Null when nothing matches.
    /// </summary>
    public static string? Match(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var exact = All.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        return name.Contains('-', StringComparison.Ordinal)
            ? null
            : All.FirstOrDefault(c => c.StartsWith(name + "-", StringComparison.OrdinalIgnoreCase));
    }
}