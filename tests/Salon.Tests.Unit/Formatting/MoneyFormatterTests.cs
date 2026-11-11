using System.Globalization;

using Salon.Core.Domain;
using Salon.Services.Formatting;

namespace Salon.Tests.Unit.Formatting;

public class MoneyFormatterTests
{
    private const string Nbsp = "\u00A0";

    [Theory]
    [InlineData("GBP", "en-GB", "45.00", "£45.00")]
    [InlineData("GBP", "en-GB", "1234.5", "£1,234.50")]
    [InlineData("GBP", "en-GB", "0", "£0.00")]
    [InlineData("PLN", "pl-PL", "45.00", "45,00" + Nbsp + "zł")]
    [InlineData("PLN", "pl-PL", "120", "120,00" + Nbsp + "zł")]
    public void A_native_currency_uses_the_cultures_number_format_and_the_currency_symbol(string currency, string culture, string amount, string expected)
    {
        var money = new Money(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);

        money.ToDisplayString(CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("PLN", "en-GB", "PLN" + Nbsp + "45.00")]
    [InlineData("GBP", "pl-PL", "GBP" + Nbsp + "45,00")]
    [InlineData("EUR", "en-GB", "EUR" + Nbsp + "45.00")]
    public void A_foreign_or_unknown_currency_shows_its_ISO_code_before_the_amount(string currency, string culture, string expected)
    {
        new Money(45m, currency).ToDisplayString(CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);
    }

    [Fact]
    public void Neutral_and_invariant_cultures_have_no_native_currency()
    {
        new Money(45m, "GBP").ToDisplayString(CultureInfo.InvariantCulture).ShouldBe("GBP" + Nbsp + "45.00");
        new Money(45m, "PLN").ToDisplayString(CultureInfo.GetCultureInfo("pl")).ShouldBe("PLN" + Nbsp + "45,00");
    }

    [Fact]
    public void Amounts_are_rounded_to_two_decimals_for_display_only()
    {
        new Money(9.999m, "GBP").ToDisplayString(CultureInfo.GetCultureInfo("en-GB")).ShouldBe("£10.00");
    }
}