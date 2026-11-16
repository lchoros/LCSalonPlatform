using NodaTime;
using Salon.Core.Staff;
using Salon.Services.Formatting;
using Salon.Services.Localization;

namespace Salon.Web.Features.Staff.ListStaff;

/// <summary>How the staff list writes a rota (ADR 0010, ADR 0043): "Tue–Fri" and "09:00–13:00, 14:00–17:00".</summary>
public static class WorkingHoursText
{
    /// <summary>One day, or the first and the last of a run of days with the same hours.</summary>
    public static string Days(ILocalizationService localizer, WorkingHoursGroup group)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(group);
        return group.From == group.To ? DayShort(localizer, group.From) : $"{DayShort(localizer, group.From)}–{DayShort(localizer, group.To)}";
    }

    public static string Ranges(WorkingHoursGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return string.Join(", ", group.Ranges.Select(r => $"{r.Start.ToClockTime()}–{r.End.ToClockTime()}"));
    }

    private static string DayShort(ILocalizationService localizer, IsoDayOfWeek day) => localizer[$"Common.DayShort.{day}"];
}