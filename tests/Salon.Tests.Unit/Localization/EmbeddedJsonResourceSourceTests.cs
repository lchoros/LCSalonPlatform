using System.Reflection;

using Salon.Services.Localization;

namespace Salon.Tests.Unit.Localization;

/// <summary>
/// Fixtures are embedded with logical names such as <c>Salon.Tests.Unit.Fixtures.Good.Localization.{culture}.json</c>
/// (one flat file per culture) and <c>Salon.Tests.Unit.Fixtures.Modular.Localization.{culture}.{Module}.json</c> (one
/// file per module and culture, ADR 0006 amendment 2026-11-12), so each root exercises one case.
/// </summary>
public class EmbeddedJsonResourceSourceTests
{
    private static readonly Assembly Self = typeof(EmbeddedJsonResourceSourceTests).Assembly;

    [Fact]
    public void Loads_one_dictionary_per_supported_culture_file()
    {
        var source = new EmbeddedJsonResourceSource(Self, "Good.Localization");

        var resources = source.Load();

        resources.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(["en-GB", "pl-PL"]);
        resources["en-GB"]["Fixture.Greeting"].ShouldBe("Hello");
        resources["en-GB"]["Fixture.Farewell"].ShouldBe("Goodbye");
        resources["pl-PL"]["Fixture.Greeting"].ShouldBe("Cześć");
        resources["pl-PL"]["Fixture.Farewell"].ShouldBe("Do widzenia");
    }

    [Fact]
    public void Module_files_in_a_culture_folder_merge_into_one_dictionary_per_culture()
    {
        var source = new EmbeddedJsonResourceSource(Self, "Modular.Localization");

        var resources = source.Load();

        resources.Keys.OrderBy(k => k, StringComparer.Ordinal).ShouldBe(["en-GB", "pl-PL"]);
        resources["en-GB"].Count.ShouldBe(3);
        resources["en-GB"]["Fixture.Greeting"].ShouldBe("Hello");
        resources["en-GB"]["Fixture.Farewell"].ShouldBe("Goodbye");
        resources["en-GB"]["Other.Thing"].ShouldBe("Thing");
        resources["pl-PL"]["Fixture.Greeting"].ShouldBe("Cześć");
        resources["pl-PL"]["Other.Thing"].ShouldBe("Rzecz");
    }

    [Fact]
    public void The_culture_folder_is_embedded_as_a_dot_segment_of_the_manifest_name()
    {
        // Proves the csproj LogicalName transform (RecursiveDir -> dot) on this platform's path separator.
        var names = Self.GetManifestResourceNames()
            .Where(n => n.Contains(".Modular.Localization.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        names.ShouldBe(
        [
            "Salon.Tests.Unit.Fixtures.Modular.Localization.en-GB.Fixture.json",
            "Salon.Tests.Unit.Fixtures.Modular.Localization.en-GB.Other.json",
            "Salon.Tests.Unit.Fixtures.Modular.Localization.pl-PL.Fixture.json",
            "Salon.Tests.Unit.Fixtures.Modular.Localization.pl-PL.Other.json",
        ]);
    }

    [Fact]
    public void ReadFiles_names_the_culture_and_module_of_every_resource()
    {
        var files = new EmbeddedJsonResourceSource(Self, "Modular.Localization").ReadFiles();

        files.Select(f => (f.Culture, f.Module))
            .OrderBy(f => f.Culture, StringComparer.Ordinal)
            .ThenBy(f => f.Module, StringComparer.Ordinal)
            .ShouldBe([("en-GB", "Fixture"), ("en-GB", "Other"), ("pl-PL", "Fixture"), ("pl-PL", "Other")]);

        var other = files.Single(f => f.Culture == "pl-PL" && f.Module == "Other");
        other.ResourceName.ShouldBe("Salon.Tests.Unit.Fixtures.Modular.Localization.pl-PL.Other.json");
        other.Entries.ShouldBe(new Dictionary<string, string> { ["Other.Thing"] = "Rzecz" });
    }

    [Fact]
    public void ReadFiles_reports_a_legacy_flat_file_without_a_module()
    {
        var files = new EmbeddedJsonResourceSource(Self, "Good.Localization").ReadFiles();

        files.Select(f => (f.Culture, f.Module))
            .OrderBy(f => f.Culture, StringComparer.Ordinal)
            .ShouldBe([("en-GB", null), ("pl-PL", null)]);
    }

    [Fact]
    public void A_legacy_flat_file_and_module_files_for_one_culture_merge()
    {
        var resources = new EmbeddedJsonResourceSource(Self, "Mixed.Localization").Load();

        resources.Keys.ShouldBe(["en-GB"]);
        resources["en-GB"]["Legacy.Greeting"].ShouldBe("Hello");
        resources["en-GB"]["Fixture.Farewell"].ShouldBe("Goodbye");
    }

    [Fact]
    public void Culture_lookup_in_the_result_ignores_case()
    {
        var resources = new EmbeddedJsonResourceSource(Self, "Good.Localization").Load();

        resources.ContainsKey("EN-gb").ShouldBeTrue();
    }

    [Fact]
    public void Name_identifies_the_assembly()
    {
        new EmbeddedJsonResourceSource(Self, "Good.Localization").Name.ShouldContain("Salon.Tests.Unit");
    }

    [Fact]
    public void A_file_for_an_unsupported_culture_fails_loudly_naming_the_resource()
    {
        var source = new EmbeddedJsonResourceSource(Self, "Bad.Localization");

        var ex = Should.Throw<InvalidOperationException>(() => source.Load());

        ex.Message.ShouldContain("de-DE");
        ex.Message.ShouldContain("Salon.Tests.Unit.Fixtures.Bad.Localization.de-DE.json");
    }

    [Fact]
    public void A_key_defined_in_two_files_of_one_culture_fails_naming_both_files_and_the_key()
    {
        var source = new EmbeddedJsonResourceSource(Self, "DupAcross.Localization");

        var ex = Should.Throw<InvalidOperationException>(() => source.Load());

        ex.Message.ShouldContain("Salon.Tests.Unit.Fixtures.DupAcross.Localization.en-GB.Fixture.json");
        ex.Message.ShouldContain("Salon.Tests.Unit.Fixtures.DupAcross.Localization.en-GB.Other.json");
        ex.Message.ShouldContain("Fixture.Greeting");
        ex.Message.ShouldContain("en-GB");
    }

    [Fact]
    public void A_key_repeated_inside_one_file_fails_naming_the_file_and_the_key()
    {
        var source = new EmbeddedJsonResourceSource(Self, "DupKey.Localization");

        var ex = Should.Throw<InvalidOperationException>(() => source.Load());

        ex.Message.ShouldContain("Salon.Tests.Unit.Fixtures.DupKey.Localization.en-GB.json");
        ex.Message.ShouldContain("Fixture.Greeting");
    }

    [Fact]
    public void An_assembly_without_resource_files_under_the_root_yields_no_cultures()
    {
        new EmbeddedJsonResourceSource(Self, "Nowhere.Localization").Load().ShouldBeEmpty();
    }
}
