using System.Text.RegularExpressions;

using Salon.Core.Localization;
using Salon.Services.Localization;

namespace Salon.Tests.Architecture;

/// <summary>
/// CLAUDE.md rule 6: all UI text goes through ILocalizationService resource keys with both en-GB and pl-PL values.
/// Loads the host's embedded resources and checks parity and shape, that every key a .cshtml anywhere in the host asks
/// for exists, and that the views no longer carry the copy as literals.
/// </summary>
public partial class LocalizationResourceTests
{
    // Lazy so a packaging error surfaces as its own message, not as a TypeInitializationException.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Resources =
        new(() => new EmbeddedJsonResourceSource(Assemblies.Web).Load());

    [Fact]
    public void The_host_ships_both_supported_cultures()
    {
        Resources.Value.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(SupportedCultures.All.OrderBy(k => k, StringComparer.Ordinal));
        Resources.Value[SupportedCultures.EnGb].ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_key_exists_in_both_cultures()
    {
        var english = Resources.Value[SupportedCultures.EnGb].Keys.ToHashSet(StringComparer.Ordinal);
        var polish = Resources.Value[SupportedCultures.PlPl].Keys.ToHashSet(StringComparer.Ordinal);

        var onlyEnglish = english.Except(polish).Order(StringComparer.Ordinal).ToList();
        var onlyPolish = polish.Except(english).Order(StringComparer.Ordinal).ToList();

        (onlyEnglish.Count + onlyPolish.Count).ShouldBe(
            0,
            $"missing in pl-PL: [{string.Join(", ", onlyEnglish)}]; missing in en-GB: [{string.Join(", ", onlyPolish)}]");
    }

    [Fact]
    public void Placeholders_match_between_cultures()
    {
        var english = Resources.Value[SupportedCultures.EnGb];
        var polish = Resources.Value[SupportedCultures.PlPl];

        var mismatched = english.Keys.Intersect(polish.Keys, StringComparer.Ordinal)
            .Where(key => !Placeholders(english[key]).SetEquals(Placeholders(polish[key])))
            .Order(StringComparer.Ordinal)
            .ToList();

        mismatched.ShouldBeEmpty($"{{n}} placeholders differ between en-GB and pl-PL for: {string.Join(", ", mismatched)}");
    }

    [Fact]
    public void No_value_is_empty()
    {
        foreach (var (culture, entries) in Resources.Value)
        {
            var empty = entries.Where(e => string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Key).ToList();
            empty.ShouldBeEmpty($"{culture} has empty values for: {string.Join(", ", empty)}");
        }
    }

    [Fact]
    public void Keys_are_dotted_PascalCase_segments()
    {
        foreach (var (culture, entries) in Resources.Value)
        {
            var bad = entries.Keys.Where(k => !KeyPattern().IsMatch(k)).ToList();
            bad.ShouldBeEmpty($"{culture} has malformed keys: {string.Join(", ", bad)}");
        }
    }

    [Fact]
    public void Every_key_a_view_asks_for_exists()
    {
        var known = Resources.Value[SupportedCultures.EnGb];
        var missing = new List<string>();

        foreach (var view in HostViews())
        {
            var text = File.ReadAllText(view);
            foreach (Match match in ViewKeyPattern().Matches(text))
            {
                var key = match.Groups["key"].Value;
                if (!known.ContainsKey(key))
                {
                    missing.Add($"{Path.GetRelativePath(HostRoot(), view)}: {key}");
                }
            }
        }

        missing.ShouldBeEmpty("views use keys that no resource file defines: " + string.Join("; ", missing));
    }

    [Fact]
    public void At_least_one_view_uses_the_localization_service()
    {
        HostViews().Any(v => ViewKeyPattern().IsMatch(File.ReadAllText(v))).ShouldBeTrue("the key regex no longer matches any view; the guard would be silent");
    }

    [Theory]
    [InlineData("Salon Platform")]
    [InlineData("Online booking")]
    public void Views_do_not_hard_code_the_copy(string literal)
    {
        var offenders = HostViews()
            .Where(f => File.ReadAllText(f).Contains(literal, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(HostRoot(), f))
            .ToList();

        offenders.ShouldBeEmpty($"'{literal}' is hard-coded in: {string.Join(", ", offenders)}");
    }

    /// <summary>Every Razor file in the host project (Views/, Features/, Areas/), skipping build output.</summary>
    private static List<string> HostViews()
    {
        var root = HostRoot();
        Directory.Exists(root).ShouldBeTrue($"host project not found at {root}");

        var views = Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .ToList();

        views.ShouldNotBeEmpty("no .cshtml files found in the host");
        return views;
    }

    private static string HostRoot() => Path.Combine(RepositoryRoot(), "src", "Presentation", "Salon.Web");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Salon.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Salon.slnx not found above " + AppContext.BaseDirectory);
    }

    private static HashSet<string> Placeholders(string value)
        => PlaceholderPattern().Matches(value).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*(\\.[A-Z][A-Za-z0-9]*)+$")]
    private static partial Regex KeyPattern();

    /// <summary>T["Key"], T.Get("Key" and T.Format("Key" as written in Razor.</summary>
    [GeneratedRegex("\\bT(?:\\[\\s*|\\.(?:Get|Format)\\(\\s*)\"(?<key>[^\"]+)\"")]
    private static partial Regex ViewKeyPattern();

    [GeneratedRegex("\\{\\d+\\}")]
    private static partial Regex PlaceholderPattern();
}