using System.Collections;
using System.Globalization;
using Salon.Mobile.Resources.Strings;
using Salon.Mobile.Screens.Contracts;

namespace Salon.Mobile.Formatting;

// The value converters the pages use, registered once in App.xaml. Each turns one API value into words or a state;
// none of them calls anything or keeps anything.

/// <summary>Base for one-way converters: XAML never writes these values back.</summary>
internal abstract class OneWayConverter : IValueConverter
{
    public abstract object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary><see cref="IPriced"/> to "£16.00" or "from £16.00".</summary>
internal sealed class PriceConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IPriced price ? Display.Price(price.Price, price.PriceIsFrom, price.Currency, culture) : string.Empty;
}

/// <summary><see cref="IServiceRow"/> to "from £38.00 · 45 min".</summary>
internal sealed class PriceAndDurationConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IServiceRow row
            ? Display.Format(culture, AppResources.PriceAndDuration, Display.Price(row.Price.Price, row.Price.PriceIsFrom, row.Price.Currency, culture), Display.Duration(row.DurationMinutes, culture))
            : string.Empty;
}

/// <summary>Minutes to "45 min", "1 h 15 min".</summary>
internal sealed class DurationConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int minutes ? Display.Duration(minutes, culture) : string.Empty;
}

/// <summary>
/// A "yyyy-MM-dd" date to one of its names. Parameter: <c>weekday</c> ("Thu"), <c>day</c> ("26"), <c>month</c>
/// ("November 2026") or <c>long</c> ("Thursday 26 November").
/// </summary>
internal sealed class DateConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var date = Display.Date(value as string);
        if (date is null)
        {
            return string.Empty;
        }

        return (parameter as string) switch
        {
            "weekday" => date.Value.ToString("ddd", culture),
            "day" => date.Value.Day.ToString(culture),
            "month" => culture.TextInfo.ToTitleCase(date.Value.ToString(culture.DateTimeFormat.YearMonthPattern, culture)),
            _ => Display.LongDate(value as string, culture),
        };
    }
}

/// <summary><see cref="IVisitSummary"/> to "Thursday 26 November at 09:45".</summary>
internal sealed class WhenConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IVisitSummary visit
            ? Display.Format(culture, AppResources.DateAtTime, Display.LongDate(visit.LocalDate, culture), visit.LocalTime)
            : string.Empty;
}

/// <summary><see cref="IVisitPart"/> to its own "09:45 – 10:30"; empty before a time is chosen.</summary>
internal sealed class TimeRangeConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IVisitPart { LocalStart: { } start, LocalEnd: { } end }
            ? Display.Format(culture, AppResources.TimeRange, start, end)
            : string.Empty;
}

/// <summary>Who with, in words: a person's name or "Anyone available" (<see cref="IStaffChoice"/>, <see cref="IVisitPart"/>, <see cref="IVisitSummary"/>).</summary>
internal sealed class StaffNameConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        IStaffChoice choice => choice.IsAnyone ? AppResources.Anyone : choice.Name,
        IVisitPart part => part.Anyone || part.StaffName is null ? AppResources.Anyone : part.StaffName,
        IVisitSummary visit => visit.StaffName ?? AppResources.Anyone,
        _ => string.Empty,
    };
}

/// <summary>A name to its initials ("Anna Nowak" to "AN"), for a circle without a photo.</summary>
internal sealed class InitialsConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var words = (value as string ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Concat(words.Take(2).Select(word => char.ToUpper(word[0], culture)));
    }
}

/// <summary><see cref="IServiceFilter"/> to its label.</summary>
internal sealed class FilterNameConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        IServiceFilter { Kind: ServiceFilterKind.All } => AppResources.FilterAll,
        IServiceFilter { Kind: ServiceFilterKind.Popular } => AppResources.FilterPopular,
        IServiceFilter filter => filter.CategoryName,
        _ => string.Empty,
    };
}

/// <summary><see cref="PartOfDay"/> to "Morning", "Afternoon", "Evening".</summary>
internal sealed class PartOfDayConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        PartOfDay.Morning => AppResources.Morning,
        PartOfDay.Afternoon => AppResources.Afternoon,
        PartOfDay.Evening => AppResources.Evening,
        _ => string.Empty,
    };
}

/// <summary><see cref="TimeHint"/> to the sentence at the top of the time screen.</summary>
internal sealed class TimeHintConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TimeHint.ChooseTime => AppResources.MissingTime,
        TimeHint.ChooseStaff => AppResources.MissingStaff,
        TimeHint.ChooseTimeAndStaff => AppResources.MissingTimeAndStaff,
        TimeHint.ChooseTimeBeforeAdding => AppResources.MissingTimeBeforeAdding,
        TimeHint.VisitFull => AppResources.VisitFull,
        _ => string.Empty,
    };
}

/// <summary>Attempts left after a wrong code to the sentence that says so; empty before.</summary>
internal sealed class AttemptsLeftConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        1 => AppResources.WrongCodeOne,
        int left => Display.Format(culture, AppResources.WrongCodeMany, left),
        _ => string.Empty,
    };
}

/// <summary>Whether a code is texted first, to the words on the details screen's button.</summary>
internal sealed class SubmitTextConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? AppResources.SendCode : AppResources.BookNow;
}

/// <summary>True when the value is there: not null, not an empty text, not an empty list, not <see cref="TimeHint.None"/>.</summary>
internal sealed class HasValueConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => Has(value) != (parameter as string == "not");

    private static bool Has(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        ICollection list => list.Count > 0,
        IEnumerable sequence => sequence.Cast<object?>().Any(),
        TimeHint hint => hint != TimeHint.None,
        _ => true,
    };
}

/// <summary>Not.</summary>
internal sealed class InvertConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>Pale until allowed: true is fully drawn, false is faint (still tappable).</summary>
internal sealed class AllowedOpacityConverter : OneWayConverter
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 1d : 0.45d;
}