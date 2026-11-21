using System.Globalization;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>How the booking form says there is nothing free (ADR 0092, ADR 0043): the words, kept out of the view.</summary>
public static class BookForClientText
{
    /// <summary>"No free times with Priya Shah on Saturday 28 November 2026." — or without a name under "anyone".</summary>
    public static string NoSlots(ILocalizationService localizer, BookForClientForm form, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(culture);
        var day = form.Day.ToDayHeading(culture);
        return form.Offering?.StaffName is { Length: > 0 } name
            ? localizer.Format("Appointments.Book.NoSlotsWith", name, day)
            : localizer.Format("Appointments.Book.NoSlotsOn", day);
    }
}