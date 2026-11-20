using System.Globalization;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Dashboard.Today;

/// <summary>How the dashboard writes a share and a person's hours (ADR 0084, ADR 0043): the words, kept out of the view.</summary>
public static class DashboardText
{
    /// <summary>A share as "31.3%", or "No data yet" when there is nothing to divide by.</summary>
    public static string Percent(ILocalizationService localizer, decimal? rate, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(culture);
        return rate is { } value ? localizer.Format("Dashboard.Percent", value.ToString("0.#", culture)) : localizer["Dashboard.NotKnown"];
    }

    /// <summary>
    /// "2.5 of 8 hours booked" for somebody who is in today; "Not in today." for somebody who is not, with the hours
    /// booked all the same when a visit stands on their day off.
    /// </summary>
    public static string Hours(ILocalizationService localizer, OccupancyRow person, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(person);
        ArgumentNullException.ThrowIfNull(culture);
        var booked = ClockFormatter.MinutesAsHours(person.BookedMinutes, culture);
        if (person.IsIn)
        {
            return localizer.Format("Dashboard.Occupancy.Booked", booked, ClockFormatter.MinutesAsHours(person.WorkingMinutes, culture));
        }

        return person.BookedMinutes > 0 ? localizer.Format("Dashboard.Occupancy.NotInBooked", booked) : localizer["Dashboard.Occupancy.NotIn"];
    }
}