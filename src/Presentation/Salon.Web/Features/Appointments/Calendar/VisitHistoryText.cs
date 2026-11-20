using System.Globalization;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>How the card writes the lines of a visit's history (ADR 0091, ADR 0043): the words, kept out of the view.</summary>
public static class VisitHistoryText
{
    /// <summary>"Sat 21 Nov, 11:15" in the salon's time.</summary>
    public static string When(LocalDateTime at, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return $"{at.Date.ToDayHeading(culture)}, {at.TimeOfDay.ToShortTime(culture)}";
    }

    /// <summary>"Moved by Studio 20 Owner", or only "Booked online" when no login did it.</summary>
    public static string What(ILocalizationService localizer, VisitHistoryLine line)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(line);
        var what = localizer[$"Appointments.Calendar.History.Kind.{line.Kind}"];
        return line.By is { Length: > 0 } by ? localizer.Format("Appointments.Calendar.History.By", what, by) : what;
    }

    /// <summary>
    /// "From Sat 21 Nov, 11:15 with Anna to Mon 23 Nov, 10:00 with Tom" for a move that recorded both ends; null for
    /// any other line and for a move from before the ends were kept.
    /// </summary>
    public static string? Move(ILocalizationService localizer, VisitHistoryLine line, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(line);
        return line is { From: { } from, To: { } to }
            ? localizer.Format("Appointments.Calendar.History.Move", Place(localizer, from, culture), Place(localizer, to, culture))
            : null;
    }

    private static string Place(ILocalizationService localizer, VisitPlace place, CultureInfo culture)
        => place.StaffName is { Length: > 0 } name
            ? localizer.Format("Appointments.Calendar.History.PlaceWith", When(place.At, culture), name)
            : When(place.At, culture);
}