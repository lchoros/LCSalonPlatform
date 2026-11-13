using System.Globalization;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Salon.Core.Domain;
using Salon.Core.Localization;
using Salon.Services.Formatting;

namespace Salon.Tests.Unit.Properties;

public class MoneyAndCulturePropertyTests
{
    [Property]
    public Property money_formatter_to_display_string_formats_gbp_correctly_in_en_gb()
    {
        var gen = Gen.Choose(0, 1_000_000_00).Select(pennies => pennies / 100m);
        return Prop.ForAll(gen.ToArbitrary(), amount =>
        {
            var money = new Money(amount, "GBP");
            var formatted = money.ToDisplayString(CultureInfo.GetCultureInfo("en-GB"));
            formatted.StartsWith('£').ShouldBeTrue();
            decimal.Parse(formatted.Replace("£", "").Replace(",", ""), NumberStyles.Currency | NumberStyles.AllowThousands, CultureInfo.GetCultureInfo("en-GB")).ShouldBe(Math.Round(amount, 2, MidpointRounding.AwayFromZero));
            formatted.Contains(' ').ShouldBeFalse();
        });
    }

    [Property]
    public Property money_formatter_to_display_string_formats_pln_correctly_in_pl_pl()
    {
        var gen = Gen.Choose(0, 1_000_000_00).Select(pennies => pennies / 100m);
        return Prop.ForAll(gen.ToArbitrary(), amount =>
        {
            var money = new Money(amount, "PLN");
            var formatted = money.ToDisplayString(CultureInfo.GetCultureInfo("pl-PL"));
            formatted.EndsWith(" zł", StringComparison.Ordinal).ShouldBeTrue();
            decimal.Parse(formatted.Replace(" zł", "").Replace(".", ","), NumberStyles.Currency | NumberStyles.AllowThousands, CultureInfo.GetCultureInfo("pl-PL")).ShouldBe(Math.Round(amount, 2, MidpointRounding.AwayFromZero));
            formatted.Contains(' ').ShouldBeFalse();
        });
    }

    [Property]
    public Property money_formatter_to_display_string_formats_eur_usd_chf_correctly_in_en_gb_and_pl_pl()
    {
        var gen = Gen.Choose(0, 1_000_000_00).Select(pennies => pennies / 100m);
        var currencyGen = Gen.Elements("EUR", "USD", "CHF");
        return Prop.ForAll(gen.ToArbitrary(), currencyGen.ToArbitrary(), (amount, currency) =>
        {
            var money = new Money(amount, currency);
            var formattedEnGb = money.ToDisplayString(CultureInfo.GetCultureInfo("en-GB"));
            var formattedPlPl = money.ToDisplayString(CultureInfo.GetCultureInfo("pl-PL"));
            formattedEnGb.StartsWith($"{currency} ", StringComparison.Ordinal).ShouldBeTrue();
            formattedPlPl.StartsWith($"{currency} ", StringComparison.Ordinal).ShouldBeTrue();
            formattedEnGb.Contains(' ').ShouldBeFalse();
            formattedPlPl.Contains(' ').ShouldBeFalse();
        });
    }

    [Property]
    public Property money_formatter_to_display_string_rounds_to_two_decimals()
    {
        var gen = Gen.Choose(0, 1_000_000_00).Select(pennies => pennies / 1000m);
        return Prop.ForAll(gen.ToArbitrary(), amount =>
        {
            var money = new Money(amount, "GBP");
            var formatted = money.ToDisplayString(CultureInfo.GetCultureInfo("en-GB"));
            var roundedAmount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
            decimal.Parse(formatted.Replace("£", "").Replace(",", ""), NumberStyles.Currency | NumberStyles.AllowThousands, CultureInfo.GetCultureInfo("en-GB")).ShouldBe(roundedAmount);
        });
    }

    [Property]
    public Property supported_cultures_match_returns_exact_match_in_any_casing()
    {
        var gen = Gen.Elements(SupportedCultures.All.ToArray());
        return Prop.ForAll(gen.ToArbitrary(), culture =>
        {
            SupportedCultures.Match(culture.ToUpperInvariant()).ShouldBe(culture);
            SupportedCultures.Match(culture.ToLowerInvariant()).ShouldBe(culture);
            SupportedCultures.Match(culture).ShouldBe(culture);
        });
    }

    [Property]
    public Property supported_cultures_match_returns_supported_culture_for_language()
    {
        var gen = Gen.Elements("en", "pl");
        return Prop.ForAll(gen.ToArbitrary(), language =>
        {
            SupportedCultures.Match(language.ToUpperInvariant()).ShouldBe(SupportedCultures.All.FirstOrDefault(c => c.StartsWith(language, StringComparison.OrdinalIgnoreCase)));
            SupportedCultures.Match(language.ToLowerInvariant()).ShouldBe(SupportedCultures.All.FirstOrDefault(c => c.StartsWith(language, StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Property]
    public Property supported_cultures_match_returns_null_for_random_strings()
    {
        var charGen = Gen.Elements('a', 'b', 'c', 'x', '-');
        var gen = Gen.Choose(1, 8).SelectMany(length => Gen.ArrayOf(charGen, length)).Select(chars => new string(chars)).Where(s => !s.StartsWith("en", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("pl", StringComparison.OrdinalIgnoreCase));
        return Prop.ForAll(gen.ToArbitrary(), randomString =>
        {
            SupportedCultures.Match(randomString).ShouldBe(null);
        });
    }

    [Property]
    public Property supported_cultures_match_never_returns_null_when_is_supported()
    {
        var gen = Gen.Elements(SupportedCultures.All.ToArray());
        return Prop.ForAll(gen.ToArbitrary(), culture =>
        {
            SupportedCultures.IsSupported(culture).ShouldBeTrue();
            SupportedCultures.Match(culture).ShouldNotBe(null);
        });
    }

    [Property]
    public Property money_add_is_commutative_and_sums_amounts()
    {
        var gen = Gen.Choose(0, 1_000_000_00).Select(pennies => pennies / 100m);
        var currencyGen = Gen.Elements("GBP", "PLN", "EUR");
        return Prop.ForAll(gen.ToArbitrary(), gen.ToArbitrary(), currencyGen.ToArbitrary(), (amount1, amount2, currency) =>
        {
            var money1 = new Money(amount1, currency);
            var money2 = new Money(amount2, currency);
            (money1.Add(money2).Amount == money2.Add(money1).Amount).ShouldBeTrue();
            (money1.Add(money2).Amount == amount1 + amount2).ShouldBeTrue();
        });
    }

    [Property]
    public Property money_add_throws_invalid_operation_exception_for_different_currencies()
    {
        var gen = Gen.Choose(0, 1_000_000_00).Select(pennies => pennies / 100m);
        var currencyGen = Gen.Elements("GBP", "PLN", "EUR");
        var differentCurrenciesGen = Gen.Zip(currencyGen, currencyGen).Where(t => t.Item1 != t.Item2);
        return Prop.ForAll(gen.ToArbitrary(), gen.ToArbitrary(), differentCurrenciesGen.ToArbitrary(), (amount1, amount2, currencies) =>
        {
            var money1 = new Money(amount1, currencies.Item1);
            var money2 = new Money(amount2, currencies.Item2);
            var exception = Should.Throw<InvalidOperationException>(() => money1.Add(money2));
            exception.Message.ShouldBe($"Cannot add {currencies.Item2} to {currencies.Item1}.");
            return true;
        });
    }
}