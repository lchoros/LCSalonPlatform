using System.Collections.Frozen;
using System.Globalization;

namespace Salon.Mobile.Core.Localization;

/// <summary>
/// Prices, lengths, days and months as the screens show them. Money follows the web's rule (Salon.Services
/// MoneyFormatter): the culture's number format with the currency's own symbol where it is the culture's native one
/// (£45.00 in en-GB, 45,00 zł in pl-PL), otherwise the ISO code before the amount. Dates are the server's plain
/// "yyyy-MM-dd" days in the salon's zone; the phone's zone is never consulted.
/// </summary>
public static class DisplayFormats
{
    private const char NoBreakSpace = ' ';
    private const string Minus = "−";
    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Day of the week, the day, the month and the year; the culture picks the month's grammatical case.</summary>
    private const string LongDateFormat = "dddd d MMMM yyyy";

    private static readonly FrozenDictionary<string, (string Symbol, bool AfterAmount)> Symbols =
        new Dictionary<string, (string, bool)>(StringComparer.Ordinal)
        {
            ["GBP"] = ("£", false),
            ["PLN"] = ("zł", true),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>"£45.00", "45,00 zł", "PLN 45.00" in en-GB.</summary>
    public static string Money(decimal amount, string currency, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(culture);

        var negative = decimal.Round(amount, 2, MidpointRounding.AwayFromZero) < 0m;
        var number = Math.Abs(amount).ToString("N2", culture);
        string text;
        if (Symbols.TryGetValue(currency, out var symbol) && string.Equals(NativeCurrency(culture), currency, StringComparison.Ordinal))
        {
            text = symbol.AfterAmount ? $"{number}{NoBreakSpace}{symbol.Symbol}" : $"{symbol.Symbol}{number}";
        }
        else
        {
            text = $"{currency}{NoBreakSpace}{number}";
        }

        return negative ? Minus + text : text;
    }

    /// <summary>"£45.00", or "from £16.00" where the people who offer it charge differently.</summary>
    public static string Price(IAppText text, decimal amount, string currency, bool isFrom)
    {
        ArgumentNullException.ThrowIfNull(text);
        var money = Money(amount, currency, text.Culture);
        return isFrom ? text.Format(TextKeys.PriceFrom, money) : money;
    }

    /// <summary>"45 min".</summary>
    public static string Minutes(IAppText text, int minutes)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Format(TextKeys.Minutes, minutes);
    }

    /// <summary>"30–60 min", or "45 min" when both ends are the same.</summary>
    public static string MinutesRange(IAppText text, int shortest, int longest)
    {
        ArgumentNullException.ThrowIfNull(text);
        return shortest == longest ? Minutes(text, shortest) : text.Format(TextKeys.MinutesRange, shortest, longest);
    }

    /// <summary>"09:45 – 10:30".</summary>
    public static string TimeRange(IAppText text, string start, string end)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Format(TextKeys.TimeRange, start, end);
    }

    /// <summary>A "yyyy-MM-dd" from the server as a plain day; null when it is not one.</summary>
    public static DateOnly? ParseDate(string? value)
    {
        return DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    }

    /// <summary>"November 2026", "Listopad 2026": the culture's year-and-month pattern, first letter capital.</summary>
    public static string MonthTitle(string? date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var day = ParseDate(date);
        if (day is null)
        {
            return string.Empty;
        }

        return Capitalise(day.Value.ToString(culture.DateTimeFormat.YearMonthPattern, culture), culture);
    }

    /// <summary>"Thu", "czw.".</summary>
    public static string Weekday(DateOnly date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.DateTimeFormat.GetAbbreviatedDayName(date.DayOfWeek);
    }

    /// <summary>"Thursday 26 November 2026", "czwartek 26 listopada 2026"; the server's text as it is when it is no date.</summary>
    public static string LongDate(string? date, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var day = ParseDate(date);
        return day is null ? date ?? string.Empty : day.Value.ToString(LongDateFormat, culture);
    }

    private static string Capitalise(string text, CultureInfo culture)
    {
        return text.Length == 0 ? text : culture.TextInfo.ToUpper(text[0]) + text[1..];
    }

    private static string? NativeCurrency(CultureInfo culture)
    {
        return culture.IsNeutralCulture || string.IsNullOrEmpty(culture.Name) ? null : new RegionInfo(culture.Name).ISOCurrencySymbol;
    }
}