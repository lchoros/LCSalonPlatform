using Salon.Core.Products;

namespace Salon.Tests.Unit.Products;

public class ProductRulesTests
{
    [Theory]
    [InlineData("  Shampoo 250 ml ", "Shampoo 250 ml")]
    [InlineData(null, "")]
    public void A_name_is_stored_trimmed(string? typed, string expected)
    {
        ProductRules.NormalizeName(typed).ShouldBe(expected);
    }

    [Theory]
    [InlineData("  Salon Care ", "Salon Care")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void An_optional_text_is_stored_trimmed_or_not_at_all(string? typed, string? expected)
    {
        ProductRules.NormalizeOptional(typed).ShouldBe(expected);
    }

    [Fact]
    public void Lengths_are_measured_after_trimming()
    {
        ProductRules.NameFits(new string('a', ProductRules.NameMaxLength) + "  ").ShouldBeTrue();
        ProductRules.NameFits(new string('a', ProductRules.NameMaxLength + 1)).ShouldBeFalse();
        ProductRules.BrandFits(new string('a', ProductRules.BrandMaxLength + 1)).ShouldBeFalse();
        ProductRules.CodeFits(new string('1', ProductRules.CodeMaxLength + 1)).ShouldBeFalse();
        ProductRules.HasName("  ").ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("0", true)]
    [InlineData("12.50", true)]
    [InlineData("100000", true)]
    [InlineData("-0.01", false)]
    [InlineData("12.505", false)]
    [InlineData("100000.01", false)]
    public void A_price_is_absent_or_a_sensible_amount_of_money(string? price, bool expected)
    {
        ProductRules.IsPriceValid(price is null ? null : decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(0, true)]
    [InlineData(10_000, true)]
    [InlineData(-1, false)]
    [InlineData(10_001, false)]
    public void A_low_stock_threshold_is_absent_or_zero_and_up(int? lowStockAt, bool expected)
    {
        ProductRules.IsLowStockAtValid(lowStockAt).ShouldBe(expected);
    }

    [Theory]
    [InlineData("shampoo", true)]
    [InlineData("SALON shampoo", true)]
    [InlineData("5012", true)]
    [InlineData("care 250", true)]
    [InlineData("conditioner", false)]
    [InlineData("shampoo conditioner", false)]
    public void A_search_finds_a_product_when_every_word_is_in_its_name_brand_or_code(string search, bool expected)
    {
        var product = new Product { Name = "Shampoo 250 ml", Brand = "Salon Care", Code = "5012345678900", Currency = "GBP" };

        ProductRules.Matches(product, ProductRules.Words(search)).ShouldBe(expected);
    }

    [Fact]
    public void An_empty_search_has_no_words_and_matches_everything()
    {
        ProductRules.Words("   ").ShouldBeEmpty();
        ProductRules.Matches(new Product { Name = "Colour tube", Currency = "GBP" }, []).ShouldBeTrue();
    }

    [Fact]
    public void A_like_literal_escapes_the_wildcards()
    {
        ProductRules.LikeLiteral(@"100%_pure\oil").ShouldBe(@"100\%\_pure\\oil");
    }
}