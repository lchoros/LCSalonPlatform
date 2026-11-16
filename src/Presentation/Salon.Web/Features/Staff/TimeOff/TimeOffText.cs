using NodaTime.Text;
using Salon.Core.Staff;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>How the time-off page writes an entry (ADR 0015, ADR 0043): its days and its hours.</summary>
public static class TimeOffText
{
    /// <summary>One day, or the first and the last day of the absence.</summary>
    public static string Dates(StaffTimeOff entry, LocalDatePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(pattern);
        return entry.FromDate == entry.ToDate ? pattern.Format(entry.FromDate) : $"{pattern.Format(entry.FromDate)}–{pattern.Format(entry.ToDate)}";
    }

    /// <summary>The hours blocked on each day, or "Whole day".</summary>
    public static string Times(ILocalizationService localizer, StaffTimeOff entry)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Range is { } range ? $"{range.Start.ToClockTime()}–{range.End.ToClockTime()}" : localizer["Staff.TimeOff.WholeDay"];
    }
}