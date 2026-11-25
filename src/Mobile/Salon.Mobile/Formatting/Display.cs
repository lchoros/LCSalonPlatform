using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Salon.Mobile.Resources.Strings;

namespace Salon.Mobile.Formatting;

/// <summary>
/// Turns the API's values into the words on screen. The API sends dates as "yyyy-MM-dd" and times as "HH:mm" already
/// in the salon's zone (BOOKING-API.md), so nothing here converts with the phone's zone: a date is only parsed to be
/// named.
/// </summary>
internal static class Display
{
    private static readonly ConcurrentDictionary<string, string> CurrencySymbols = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, CompositeFormat> Formats = new(StringComparer.Ordinal);

    /// <summary>string.Format over a resource text, parsed once per text (so once per language).</summary>
    public static string Format(CultureInfo culture, string format, params object?[] args) =>
        string.Format(culture, Formats.GetOrAdd(format, CompositeFormat.Parse), args);

    public static string Money(decimal amount, string currency, CultureInfo culture)
    {
        var format = (NumberFormatInfo)culture.NumberFormat.Clone();
        format.CurrencySymbol = CurrencySymbol(currency);
        return amount.ToString("C", format);
    }

    public static string Price(decimal amount, bool isFrom, string currency, CultureInfo culture)
    {
        var money = Money(amount, currency, culture);
        return isFrom ? Format(culture, AppResources.PriceFrom, money) : money;
    }

    public static string Duration(int minutes, CultureInfo culture)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        if (hours == 0)
        {
            return Format(culture, AppResources.DurationMinutes, minutes);
        }

        return rest == 0
            ? Format(culture, AppResources.DurationHours, hours)
            : Format(culture, AppResources.DurationHoursMinutes, hours, rest);
    }

    public static DateOnly? Date(string? isoDate) =>
        DateOnly.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    /// <summary>"Thursday 26 November" (en-GB) or "czwartek, 26 listopada" (pl-PL).</summary>
    public static string LongDate(string? isoDate, CultureInfo culture) =>
        Date(isoDate)?.ToString(AppResources.DateLongFormat, culture) ?? string.Empty;

    /// <summary>The currency's own sign ("£", "zł") where a region of the world uses it; the code otherwise.</summary>
    private static string CurrencySymbol(string currency) =>
        CurrencySymbols.GetOrAdd(currency, code =>
        {
            foreach (var culture in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
            {
                try
                {
                    var region = new RegionInfo(culture.Name);
                    if (string.Equals(region.ISOCurrencySymbol, code, StringComparison.OrdinalIgnoreCase))
                    {
                        return region.CurrencySymbol;
                    }
                }
                catch (ArgumentException)
                {
                    // A culture without a region of its own: not the one.
                }
            }

            return code;
        });
}