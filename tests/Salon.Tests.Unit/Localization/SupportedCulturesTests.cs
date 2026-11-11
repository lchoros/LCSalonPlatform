using Salon.Core.Localization;

namespace Salon.Tests.Unit.Localization;

public class SupportedCulturesTests
{
    [Fact]
    public void English_is_the_default_and_both_cultures_are_listed()
    {
        SupportedCultures.Default.ShouldBe("en-GB");
        SupportedCultures.All.ShouldBe(["en-GB", "pl-PL"]);
    }

    [Theory]
    [InlineData("en-GB", true)]
    [InlineData("pl-PL", true)]
    [InlineData("PL-pl", true)]
    [InlineData("pl", false)]
    [InlineData("de-DE", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSupported_accepts_only_the_two_specific_cultures(string? name, bool expected)
    {
        SupportedCultures.IsSupported(name).ShouldBe(expected);
    }

    [Theory]
    [InlineData("en-GB", "en-GB")]
    [InlineData("pl-pl", "pl-PL")]
    [InlineData("pl", "pl-PL")]
    [InlineData("EN", "en-GB")]
    [InlineData("en-US", null)]
    [InlineData("de", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Match_maps_exact_and_neutral_names_onto_a_supported_culture(string? name, string? expected)
    {
        SupportedCultures.Match(name).ShouldBe(expected);
    }
}