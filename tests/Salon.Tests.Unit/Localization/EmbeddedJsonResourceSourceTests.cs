using System.Reflection;

using Salon.Services.Localization;

namespace Salon.Tests.Unit.Localization;

/// <summary>
/// Fixtures are embedded with logical names <c>Salon.Tests.Unit.Fixtures.Good.Localization.{culture}.json</c> and
/// <c>Salon.Tests.Unit.Fixtures.Bad.Localization.de-DE.json</c>, so each root exercises one case.
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
    public void Two_resources_for_the_same_culture_fail_naming_both()
    {
        var source = new EmbeddedJsonResourceSource(Self, "DupCulture.Localization");

        var ex = Should.Throw<InvalidOperationException>(() => source.Load());

        ex.Message.ShouldContain("Salon.Tests.Unit.Fixtures.DupCulture.Localization.en-GB.json");
        ex.Message.ShouldContain("Salon.Tests.Unit.Fixtures.Second.DupCulture.Localization.en-GB.json");
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