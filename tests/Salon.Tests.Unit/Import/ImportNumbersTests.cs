using Salon.Core.Import;

namespace Salon.Tests.Unit.Import;

/// <summary>ADR 0045: numbers of a product file are read as a person typed them into a spreadsheet, in either notation.</summary>
public class ImportNumbersTests
{
    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("12,50", 12.50)]
    [InlineData("12", 12)]
    [InlineData("12.5", 12.5)]
    [InlineData("12,5", 12.5)]
    [InlineData("£12.50", 12.50)]
    [InlineData("12.50 GBP", 12.50)]
    [InlineData("12,50 zł", 12.50)]
    [InlineData(" 9 ", 9)]
    [InlineData("1,250.00", 1250)]
    [InlineData("1.250,00", 1250)]
    [InlineData("1 250,00", 1250)]
    [InlineData("1,250", 1250)]
    [InlineData("1.250", 1250)]
    [InlineData("1,250,000", 1250000)]
    [InlineData("0", 0)]
    [InlineData("0.99", 0.99)]
    public void A_price_is_read_in_either_notation(string text, double expected)
    {
        ImportNumbers.TryMoney(text, out var value).ShouldBeTrue(text);
        value.ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_cell_is_no_number_and_no_mistake(string? text)
    {
        ImportNumbers.TryMoney(text, out var money).ShouldBeTrue();
        money.ShouldBeNull();
        ImportNumbers.TryWhole(text, out var whole).ShouldBeTrue();
        whole.ShouldBeNull();
    }

    [Theory]
    [InlineData("twelve")]
    [InlineData("-5")]
    [InlineData("(5.00)")]
    [InlineData("12.50.3x")]
    [InlineData(".")]
    [InlineData(",50")]
    [InlineData("50%")]
    [InlineData("1/2")]
    public void What_is_not_a_price_is_refused(string text)
    {
        ImportNumbers.TryMoney(text, out var value).ShouldBeFalse(text);
        value.ShouldBeNull();
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("12.00", 12)]
    [InlineData("12,0", 12)]
    [InlineData("1,250", 1250)]
    [InlineData("0", 0)]
    public void A_count_is_a_whole_number(string text, int expected)
    {
        ImportNumbers.TryWhole(text, out var value).ShouldBeTrue(text);
        value.ShouldBe(expected);
    }

    [Theory]
    [InlineData("2.5")]
    [InlineData("a few")]
    [InlineData("-1")]
    [InlineData("99999999999")]
    public void Half_a_bottle_and_words_are_not_a_count(string text)
    {
        ImportNumbers.TryWhole(text, out var value).ShouldBeFalse(text);
        value.ShouldBeNull();
    }
}