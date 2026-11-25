using System.Globalization;
using Salon.Services.Localization;

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

    /// <summary>The chosen person, or "No preference" under "anyone available".</summary>
    public static string Who(ILocalizationService localizer, ChooseSlotResult result)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(result);
        return result.Staff.FirstOrDefault(person => person.Id == result.SelectedStaffId)?.DisplayName
            ?? localizer[result.NeedsStaffChoice ? "Booking.Public.StaffNotChosen" : "Booking.Public.NoPreference"];
    }
}