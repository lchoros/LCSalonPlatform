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

    /// <summary>The chosen person, or "No preference" under "anyone available".</summary>
    public static string Who(ILocalizationService localizer, ChooseSlotResult result)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(result);
        return result.Staff.FirstOrDefault(person => person.Id == result.SelectedStaffId)?.DisplayName
            ?? localizer[result.NeedsStaffChoice ? "Booking.Public.StaffNotChosen" : "Booking.Public.NoPreference"];
    }
}