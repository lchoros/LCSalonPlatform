namespace Salon.Services.Clients;

/// <summary>
/// One shape for every stored and searched number (ADR 0011): a leading <c>+</c> and 7–15 digits. Spaces, dashes, dots and
/// parentheses are dropped, a <c>00</c> prefix becomes <c>+</c>, the UK "(0)" trunk convention is removed. A number without a
/// country code resolves only when the salon's country is known from its currency: <c>GBP</c> → UK (<c>0…</c>, eleven
/// digits → <c>+44…</c>), <c>PLN</c> → Poland (nine digits starting 5–8 → <c>+48…</c>). Anything else needs an explicit <c>+</c>.
/// </summary>
public static class PhoneNumber
{
    public const int MinDigits = 7;
    public const int MaxDigits = 15;

    private const string UkCurrency = "GBP";
    private const string PolishCurrency = "PLN";

    /// <summary>Null for a blank input; otherwise the normalised number or a <see cref="FormatException"/>.</summary>
    public static string? Normalise(string? raw, string? currencyHint)
        => TryNormalise(raw, currencyHint, out var normalised)
            ? normalised
            : throw new FormatException("The phone number is not valid; use the country code for numbers from abroad.");

    /// <summary>True with a null <paramref name="normalised"/> for a blank input; false when the text is not a usable number.</summary>
    public static bool TryNormalise(string? raw, string? currencyHint, out string? normalised)
    {
        normalised = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var compact = Compact(raw);
        if (compact.StartsWith("00", StringComparison.Ordinal))
        {
            compact = string.Concat("+", compact.AsSpan(2));
        }

        if (compact.StartsWith('+'))
        {
            var digits = compact[1..];
            if (!IsDigits(digits) || digits.Length is < MinDigits or > MaxDigits)
            {
                return false;
            }

            normalised = "+" + digits;
            return true;
        }

        if (!IsDigits(compact))
        {
            return false;
        }

        normalised = currencyHint switch
        {
            UkCurrency when compact.Length == 11 && compact[0] == '0' => "+44" + compact[1..],
            PolishCurrency when compact.Length == 9 && compact[0] is >= '5' and <= '8' => "+48" + compact,
            _ => null,
        };
        return normalised is not null;
    }

    /// <summary>Only the digits of <paramref name="raw"/>, for matching a typed fragment against stored numbers.</summary>
    public static string Digits(string? raw)
        => raw is null ? string.Empty : new string(raw.Where(char.IsAsciiDigit).ToArray());

    private static string Compact(string raw)
    {
        var withoutTrunk = raw.Replace("(0)", string.Empty, StringComparison.Ordinal);
        return new string(withoutTrunk.Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '.' or '(' or ')')).ToArray());
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(char.IsAsciiDigit);
}