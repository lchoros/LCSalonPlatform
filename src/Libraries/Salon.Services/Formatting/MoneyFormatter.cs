using System.Collections.Frozen;
using System.Globalization;

using Salon.Core.Domain;

namespace Salon.Services.Formatting;

/// <summary>
/// Shows a <see cref="Money"/> the way the reader expects: the culture's number format with the currency's own symbol
/// when the currency is the culture's native one (£45.00 in en-GB, 45,00 zł in pl-PL), otherwise the ISO code before
/// the amount (PLN 45.00 in en-GB) so a foreign price is never mistaken for a local one. Spaces are no-break spaces.
/// </summary>
public static class MoneyFormatter
{
    private const char NoBreakSpace = '\u00A0';

    /// <summary>A real minus sign in front of everything (\u2212\u00A310.00), never between the symbol and the amount.</summary>
    private const string Minus = "\u2212";

    private static readonly FrozenDictionary<string, (string Symbol, bool AfterAmount)> Symbols =
        new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
        {
            ["GBP"] = ("£", false),
            ["PLN"] = ("zł", true),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string ToDisplayString(this Money money, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        // Decided on the rounded amount, so half a penny below zero does not show as "minus nothing".
        var negative = decimal.Round(money.Amount, 2, MidpointRounding.AwayFromZero) < 0m;
        var text = Format(money with { Amount = Math.Abs(money.Amount) }, culture);
        return negative ? Minus + text : text;
    }

    private static string Format(Money money, CultureInfo culture)
    {
        var amount = money.Amount.ToString("N2", culture);
        if (Symbols.TryGetValue(money.Currency, out var symbol) && string.Equals(NativeCurrency(culture), money.Currency, StringComparison.Ordinal))
        {
            return symbol.AfterAmount ? $"{amount}{NoBreakSpace}{symbol.Symbol}" : $"{symbol.Symbol}{amount}";
        }

        return $"{money.Currency}{NoBreakSpace}{amount}";
    }

    /// <summary>The ISO code of the culture's region, or null for neutral and invariant cultures, which have no region.</summary>
    private static string? NativeCurrency(CultureInfo culture)
        => culture.IsNeutralCulture || string.IsNullOrEmpty(culture.Name) ? null : new RegionInfo(culture.Name).ISOCurrencySymbol;
}