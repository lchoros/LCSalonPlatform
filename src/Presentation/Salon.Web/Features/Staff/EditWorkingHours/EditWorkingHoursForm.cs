using System.Globalization;

using NodaTime;

using Salon.Core.Staff;

namespace Salon.Web.Features.Staff.EditWorkingHours;

/// <summary>
/// The hours form as rendered and as posted: seven days Monday to Sunday, each with a "day off" box and up to
/// <see cref="WorkingHoursRules.MaxRangesPerDay"/> ranges typed as <c>&lt;input type="time"&gt;</c> text. Mutable with
/// settable collections because MVC binds <c>Days[i].Ranges[j].Start</c> into it; the controller maps it to
/// <see cref="EditWorkingHoursCommand"/>, whose failures carry the same property paths back to the fields.
/// </summary>
public sealed class EditWorkingHoursForm
{
    /// <summary>From the route, not the form body.</summary>
    public Guid StaffMemberId { get; set; }

    /// <summary>Display only; the controller fills it from the database on every render.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public IList<DayHoursForm> Days { get; init; } = [];

    /// <summary>The form for an existing rota: a day without rows is a day off, missing range slots are blank.</summary>
    public static EditWorkingHoursForm From(StaffRota rota)
    {
        ArgumentNullException.ThrowIfNull(rota);

        var form = new EditWorkingHoursForm { StaffMemberId = rota.StaffMemberId, DisplayName = rota.DisplayName };
        foreach (var day in WorkingHoursRules.Week)
        {
            var ranges = rota.Hours.Where(h => h.Day == day).OrderBy(h => h.Start).Select(h => new TimeRangeForm(h.Start, h.End)).ToList();
            var dayForm = new DayHoursForm { Day = day, IsDayOff = ranges.Count == 0 };
            foreach (var range in ranges)
            {
                dayForm.Ranges.Add(range);
            }

            dayForm.PadRanges();
            form.Days.Add(dayForm);
        }

        return form;
    }

    /// <summary>Fills in whatever a (possibly tampered) post left out, so the view can always render seven days with every slot.</summary>
    public void PadForRender()
    {
        foreach (var day in Days)
        {
            day.PadRanges();
        }
    }

    public EditWorkingHoursCommand ToCommand()
        => new(StaffMemberId, Days.Select(d => new DayHoursInput(d.Day, d.IsDayOff, d.Ranges.Select(r => new TimeRangeInput(r.Start, r.End)).ToList())).ToList());
}

public sealed class DayHoursForm
{
    public IsoDayOfWeek Day { get; set; }

    public bool IsDayOff { get; set; }

    public IList<TimeRangeForm> Ranges { get; init; } = [];

    internal void PadRanges()
    {
        while (Ranges.Count < WorkingHoursRules.MaxRangesPerDay)
        {
            Ranges.Add(new TimeRangeForm());
        }
    }
}

public sealed class TimeRangeForm
{
    public TimeRangeForm()
    {
    }

    public TimeRangeForm(LocalTime start, LocalTime end)
    {
        Start = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        End = end.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>"HH:mm" as the time input posts it; blank for an unused slot.</summary>
    public string? Start { get; set; }

    public string? End { get; set; }
}