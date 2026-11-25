using System.Globalization;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking.Times;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>How the time page words its summary (ADR 0094, ADR 0043): the words, kept out of the view.</summary>
public static class ChooseSlotText
{
    /// <summary>"09:00 – 09:45" once a time is tapped; until then how long the service takes.</summary>
    public static string When(ILocalizationService localizer, ChooseSlotResult result, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(culture);
        var minutes = (int)result.Duration.TotalMinutes;
        return result.Chosen is { } picked
            ? $"{picked.LocalStart.ToString("HH:mm", culture)} – {picked.LocalStart.PlusMinutes(minutes).ToString("HH:mm", culture)}"
            : localizer.Format("Booking.Public.DurationMinutes", minutes);
    }

    /// <summary>
    /// One service of the visit: its own "09:00 – 09:45" once a time is tapped — each begins where the one before
    /// ends — and until then how long it takes.
    /// </summary>
    public static string LineWhen(ILocalizationService localizer, ChooseSlotResult result, VisitLine line, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(culture);
        var minutes = (int)line.Duration.TotalMinutes;
        if (result.Chosen is not { } picked)
        {
            return localizer.Format("Booking.Public.DurationMinutes", minutes);
        }

        var before = (int)result.Lines.TakeWhile(l => l.ServiceId != line.ServiceId).Sum(l => l.Duration.TotalMinutes);
        var start = picked.LocalStart.PlusMinutes(before);
        return $"{start.ToString("HH:mm", culture)} – {start.PlusMinutes(minutes).ToString("HH:mm", culture)}";
    }

    /// <summary>What the client still has to choose before going on or adding a service: a time, who with, or both.</summary>
    public static string MissingKey(ChooseSlotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return (result.Chosen is null, result.NeedsStaffChoice) switch
        {
            (true, true) => "Booking.Public.Missing.TimeAndStaff",
            (false, true) => "Booking.Public.Missing.Staff",
            _ => "Booking.Public.Missing.Time",
        };
    }

    /// <summary>Who does one service: the chosen person, "No preference" under "anyone available", or that the answer is still owed.</summary>
    public static string Who(ILocalizationService localizer, VisitLine line)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(line);
        return line.Person?.DisplayName ?? localizer[line.NeedsStaffChoice ? "Booking.Public.StaffNotChosen" : "Booking.Public.NoPreference"];
    }
}