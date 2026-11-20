using Salon.Core.Domain;

namespace Salon.Plugin.Payments.Stripe;

/// <summary>
/// Money as the app keeps it (a decimal and an ISO code) against money as Stripe writes it (a whole number of the
/// smallest unit and a lower-case code). Pure. Only the currencies the salons price in are mapped, all with a
/// hundred to the unit; any other currency is refused instead of guessed, because Stripe counts some in whole units
/// and some in thousandths.
/// </summary>
public static class StripeAmounts
{
    private const decimal MinorPerUnit = 100m;

    /// <summary>ISO codes this plugin charges in: pounds, zloty, euro.</summary>
    public static IReadOnlyList<string> Currencies { get; } = ["GBP", "PLN", "EUR"];

    public static bool IsSupported(string? currency)
        => currency is not null && Currencies.Contains(currency, StringComparer.Ordinal);

    /// <summary>
    /// The amount in pence or grosze and Stripe's code for the currency. False for a currency that is not mapped, an
    /// amount that is not above zero, or one with a fraction of a penny: such an amount is never rounded into a charge.
    /// </summary>
    public static bool TryToStripe(Money money, out long amountMinor, out string currency)
    {
        amountMinor = 0;
        currency = string.Empty;
        if (!IsSupported(money.Currency) || money.Amount <= 0m)
        {
            return false;
        }

        var minor = money.Amount * MinorPerUnit;
        if (decimal.Truncate(minor) != minor || minor > long.MaxValue)
        {
            return false;
        }

        amountMinor = (long)minor;
        currency = money.Currency.ToLowerInvariant();
        return true;
    }

    /// <summary>The amount Stripe reports as the app keeps it. False for a currency that is not mapped or an amount that is not above zero.</summary>
    public static bool TryFromStripe(long? amountMinor, string? currency, out Money money)
    {
        money = default;
        if (amountMinor is not > 0 || string.IsNullOrWhiteSpace(currency))
        {
            return false;
        }

        var code = currency.ToUpperInvariant();
        if (!IsSupported(code))
        {
            return false;
        }

        money = new Money(amountMinor.Value / MinorPerUnit, code);
        return true;
    }
}