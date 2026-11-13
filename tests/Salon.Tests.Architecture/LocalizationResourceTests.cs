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
    private const string SplitScript = "tools/split-localization.ps1";

    // Lazy so a packaging error surfaces as its own message, not as a TypeInitializationException.
    private static readonly Lazy<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> Resources =
        new(() => new EmbeddedJsonResourceSource(Assemblies.Web).Load());

    private static readonly Lazy<IReadOnlyList<LocalizationResourceFile>> Files =
        new(() => new EmbeddedJsonResourceSource(Assemblies.Web).ReadFiles());

    [Fact]
    public void The_host_ships_both_supported_cultures()
    {
        Resources.Value.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(SupportedCultures.All.OrderBy(k => k, StringComparer.Ordinal));
        Resources.Value[SupportedCultures.EnGb].ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_module_file_exists_in_both_cultures()
    {
        // One file per module and culture (ADR 0006, amendment 2026-11-12): Localization/<culture>/<Module>.json.
        var english = ModulesOf(SupportedCultures.EnGb);
        var polish = ModulesOf(SupportedCultures.PlPl);

        var onlyEnglish = english.Except(polish).Order(StringComparer.Ordinal).Select(m => $"Localization/{SupportedCultures.PlPl}/{m}.json").ToList();
        var onlyPolish = polish.Except(english).Order(StringComparer.Ordinal).Select(m => $"Localization/{SupportedCultures.EnGb}/{m}.json").ToList();

        onlyEnglish.Concat(onlyPolish).ShouldBeEmpty(
            "every module file must exist for both cultures; missing files: " + string.Join(", ", onlyEnglish.Concat(onlyPolish)));
        english.ShouldNotBeEmpty("the host embeds no module files; expected Localization/<culture>/<Module>.json");
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
    public void The_host_has_no_flat_culture_file()
    {
        // A flat Localization/<culture>.json reappears when a branch from before the split is merged; it would duplicate
        // every key the module files already hold. Re-run the split script instead of resolving by hand.
        var flat = Files.Value.Where(f => f.Module is null).Select(f => f.ResourceName).Order(StringComparer.Ordinal).ToList();

        flat.ShouldBeEmpty($"flat culture files are embedded: [{string.Join(", ", flat)}]; run `pwsh {SplitScript}` and delete them");
    }

    [Fact]
    public void Every_key_lives_in_the_module_file_named_after_its_first_segment()
    {
        var misplaced = Files.Value
            .Where(f => f.Module is not null)
            .SelectMany(f => f.Entries.Keys
                .Where(key => !string.Equals(key.Split('.')[0], f.Module, StringComparison.Ordinal))
                .Select(key => $"{f.ResourceName}: {key}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        misplaced.ShouldBeEmpty("keys must sit in Localization/<culture>/<FirstSegment>.json; misplaced: " + string.Join("; ", misplaced));
    }

    [Fact]
    public void Module_file_names_are_a_single_PascalCase_segment()
    {
        var bad = Files.Value
            .Where(f => f.Module is not null && !ModulePattern().IsMatch(f.Module))
            .Select(f => f.ResourceName)
            .Order(StringComparer.Ordinal)
            .ToList();

        bad.ShouldBeEmpty("module files are Localization/<culture>/<Module>.json with a PascalCase name and no sub-folder; offenders: " + string.Join(", ", bad));
    }

    [Fact]
    public void Manifest_names_turn_the_culture_folder_into_a_dot_segment()
    {
        // The csproj LogicalName transform; a regression here would make the source see "en-GB\Catalog" as a culture.
        var names = Assemblies.Web.GetManifestResourceNames()
            .Where(n => n.Contains(".Localization.", StringComparison.Ordinal))
            .ToList();

        names.ShouldContain("Salon.Web.Localization.en-GB.Common.json");
        names.ShouldContain("Salon.Web.Localization.pl-PL.Common.json");
        names.Where(n => n.Contains('\\', StringComparison.Ordinal) || n.Contains('/', StringComparison.Ordinal)).ShouldBeEmpty();
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

    private static HashSet<string> ModulesOf(string culture)
        => Files.Value
            .Where(f => string.Equals(f.Culture, culture, StringComparison.OrdinalIgnoreCase) && f.Module is not null)
            .Select(f => f.Module!)
            .ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*(\\.[A-Z][A-Za-z0-9]*)+$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Z][A-Za-z0-9]*$")]
    private static partial Regex ModulePattern();

    /// <summary>T["Key"], T.Get("Key" and T.Format("Key" as written in Razor.</summary>
    [GeneratedRegex("\\bT(?:\\[\\s*|\\.(?:Get|Format)\\(\\s*)\"(?<key>[^\"]+)\"")]
    private static partial Regex ViewKeyPattern();

    [GeneratedRegex("\\{\\d+\\}")]
    private static partial Regex PlaceholderPattern();
}