using Salon.Core.Tenancy;

namespace Salon.Tests.Unit.Tenancy;

/// <summary>
/// A salon's web address (ADR 0075): a clean DNS label of 3 to 40 characters, lower-cased and trimmed before any
/// rule looks at it, and the platform's own names are kept back.
/// </summary>
public class TenantSlugRulesTests
{
    [Theory]
    [InlineData("studio-20")]
    [InlineData("abc")]
    [InlineData("a-1-b")]
    [InlineData("salon2")]
    public void A_clean_label_is_well_formed(string slug) => TenantSlugRules.IsWellFormed(slug).ShouldBeTrue(slug);

    [Theory]
    [InlineData("ab", "too short")]
    [InlineData("-studio", "starts with a hyphen")]
    [InlineData("studio-", "ends with a hyphen")]
    [InlineData("stu--dio", "double hyphen")]
    [InlineData("studio 20", "a space")]
    [InlineData("studio.20", "a dot would make another label")]
    [InlineData("Studio", "upper case never reaches the rule un-normalized")]
    [InlineData("salon_20", "an underscore is not DNS")]
    [InlineData("żółć", "letters a-z only")]
    public void Anything_else_is_not(string slug, string because) => TenantSlugRules.IsWellFormed(slug).ShouldBeFalse(because);

    [Fact]
    public void Longer_than_forty_characters_is_not_well_formed()
        => TenantSlugRules.IsWellFormed(new string('a', TenantSlugRules.MaxLength + 1)).ShouldBeFalse();

    [Fact]
    public void Normalize_trims_and_lowers()
        => TenantSlugRules.Normalize("  Studio-20 ").ShouldBe("studio-20");

    [Theory]
    [InlineData("www")]
    [InlineData("api")]
    [InlineData("admin")]
    [InlineData("signup")]
    [InlineData("demo")]
    public void The_platforms_own_names_are_kept_back(string slug) => TenantSlugRules.IsReserved(slug).ShouldBeTrue(slug);

    [Fact]
    public void A_salons_own_name_is_not_reserved() => TenantSlugRules.IsReserved("studio-20").ShouldBeFalse();
}