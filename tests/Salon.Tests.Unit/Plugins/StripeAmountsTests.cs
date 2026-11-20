using Salon.Core.Domain;
using Salon.Plugin.Payments.Stripe;

namespace Salon.Tests.Unit.Plugins;

/// <summary>
/// Money between the app (a decimal and an ISO code) and Stripe (pence and a lower-case code), ADR 0082. A deposit is
/// charged to the penny or not at all: an amount is never rounded into a charge and a currency is never guessed.
/// </summary>
public class StripeAmountsTests
{
    [Theory]
    [InlineData(9, "GBP", 900, "gbp")]
    [InlineData(11.25, "GBP", 1125, "gbp")]
    [InlineData(0.01, "GBP", 1, "gbp")]
    [InlineData(45.5, "PLN", 4550, "pln")]
    [InlineData(180, "EUR", 18000, "eur")]
    public void A_deposit_goes_to_Stripe_in_the_smallest_unit_and_a_lower_case_code(decimal amount, string currency, long expectedMinor, string expectedCode)
    {
        StripeAmounts.TryToStripe(new Money(amount, currency), out var minor, out var code).ShouldBeTrue();

        minor.ShouldBe(expectedMinor);
        code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(0, "GBP", "nothing is not a charge")]
    [InlineData(-5, "GBP", "nor is a negative amount")]
    [InlineData(9.005, "GBP", "a fraction of a penny is never rounded into a charge")]
    [InlineData(9, "JPY", "Stripe counts yen in whole units; a currency that is not mapped is refused, not guessed")]
    [InlineData(9, "KWD", "and dinars in thousandths")]
    [InlineData(9, "gbp", "the app's codes are upper case")]
    [InlineData(9, "", "no currency, no charge")]
    public void Anything_else_is_refused(decimal amount, string currency, string because)
    {
        StripeAmounts.TryToStripe(new Money(amount, currency), out var minor, out var code).ShouldBeFalse(because);

        minor.ShouldBe(0);
        code.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(900, "gbp", 9, "GBP")]
    [InlineData(1125, "gbp", 11.25, "GBP")]
    [InlineData(1, "pln", 0.01, "PLN")]
    [InlineData(18000, "EUR", 180, "EUR")]
    public void What_Stripe_says_was_paid_comes_back_as_the_apps_money(long minor, string code, decimal expectedAmount, string expectedCurrency)
    {
        StripeAmounts.TryFromStripe(minor, code, out var money).ShouldBeTrue();

        money.ShouldBe(new Money(expectedAmount, expectedCurrency));
    }

    [Theory]
    [InlineData(null, "gbp")]
    [InlineData(0L, "gbp")]
    [InlineData(-900L, "gbp")]
    [InlineData(900L, null)]
    [InlineData(900L, "")]
    [InlineData(900L, "jpy")]
    public void A_total_that_is_missing_empty_or_in_an_unmapped_currency_is_not_money(long? minor, string? code)
    {
        StripeAmounts.TryFromStripe(minor, code, out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(4.99)]
    [InlineData(9)]
    [InlineData(123.45)]
    [InlineData(99999.99)]
    public void There_and_back_is_the_same_money(decimal amount)
    {
        foreach (var currency in StripeAmounts.Currencies)
        {
            var money = new Money(amount, currency);

            StripeAmounts.TryToStripe(money, out var minor, out var code).ShouldBeTrue();
            StripeAmounts.TryFromStripe(minor, code, out var back).ShouldBeTrue();

            back.ShouldBe(money);
        }
    }
}