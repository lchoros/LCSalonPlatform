using NodaTime;
using NodaTime.Text;

namespace Salon.Core.Staff;

/// <summary>
/// The pure rules of a time-off entry (ADR 0015). The TimeOff validator turns each failure into a localized message; these
/// methods only decide. A span runs from one local date to another (inclusive, at most <see cref="MaxSpanDays"/> days);
/// either both times are blank (whole days) or both are set, on the rota's 5-minute grid, with the end after the start.
/// <see cref="ToIntervals"/> turns an entry into the busy intervals the availability engine excludes, one per day.
/// </summary>
public static class StaffTimeOffRules
{
    /// <summary>A whole year including a leap day; longer absences are entered as two rows.</summary>
    public const int MaxSpanDays = 366;

    private static readonly LocalDatePattern DatePattern = LocalDatePattern.Iso;

    /// <summary>Parses what an <c>&lt;input type="date"&gt;</c> posts ("2026-03-29"); null for anything else.</summary>
    public static LocalDate? TryParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var result = DatePattern.Parse(text.Trim());
        return result.Success ? result.Value : null;
    }

    /// <summary>Days in the span, both ends included; 1 for a single day, 0 or less when the span is inverted.</summary>
    public static int DayCount(LocalDate from, LocalDate to) => Period.DaysBetween(from, to) + 1;

    /// <summary>True when the span ends on or after the day it starts.</summary>
    public static bool EndsOnOrAfterStart(LocalDate from, LocalDate to) => to >= from;

    /// <summary>True when the span is no longer than <see cref="MaxSpanDays"/> days.</summary>
    public static bool FitsMaxSpan(LocalDate from, LocalDate to) => DayCount(from, to) <= MaxSpanDays;

    /// <summary>True when both times are blank (whole days) or both are set (a daily time range); one of the two is a mistake.</summary>
    public static bool TimesAreComplete(LocalTime? start, LocalTime? end) => start.HasValue == end.HasValue;

    /// <summary>True when the reason is blank or within <see cref="StaffTimeOff.ReasonMaxLength"/> characters after trimming.</summary>
    public static bool ReasonFits(string? reason) => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length <= StaffTimeOff.ReasonMaxLength;

    /// <summary>The trimmed reason, or null when blank.</summary>
    public static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    /// <summary>
    /// Every rule at once, for callers that want the full picture (tests, imports). The validator reports the same checks
    /// field by field. Times are checked only when both are present; <see cref="StaffTimeOffProblem.TimesIncomplete"/>
    /// covers the half-filled case.
    /// </summary>
    public static IReadOnlyList<StaffTimeOffProblem> Problems(LocalDate from, LocalDate to, LocalTime? start, LocalTime? end, string? reason)
    {
        var problems = new List<StaffTimeOffProblem>();
        if (!EndsOnOrAfterStart(from, to))
        {
            problems.Add(StaffTimeOffProblem.ToBeforeFrom);
        }
        else if (!FitsMaxSpan(from, to))
        {
            problems.Add(StaffTimeOffProblem.SpanTooLong);
        }

        if (!TimesAreComplete(start, end))
        {
            problems.Add(StaffTimeOffProblem.TimesIncomplete);
        }
        else if (start is { } s && end is { } e)
        {
            if (!WorkingHoursRules.IsOnStep(s) || !WorkingHoursRules.IsOnStep(e))
            {
                problems.Add(StaffTimeOffProblem.OffStep);
            }

            if (!WorkingHoursRules.EndsAfterStart(new TimeRange(s, e)))
            {
                problems.Add(StaffTimeOffProblem.EndNotAfterStart);
            }
        }

        if (!ReasonFits(reason))
        {
            problems.Add(StaffTimeOffProblem.ReasonTooLong);
        }

        return problems;
    }

    /// <summary>
    /// True when two entries of one person share at least one moment: their date spans intersect and either is whole-day,
    /// or their daily time ranges overlap (ranges that merely touch do not). Entries of different people never overlap.
    /// </summary>
    public static bool Overlaps(StaffTimeOff a, StaffTimeOff b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a.StaffMemberId != b.StaffMemberId || a.FromDate > b.ToDate || b.FromDate > a.ToDate)
        {
            return false;
        }

        return a.Range is not { } rangeA || b.Range is not { } rangeB || rangeA.Overlaps(rangeB);
    }

    /// <summary>
    /// The busy intervals of one entry in the salon's zone, one per day of the span, ordered by start. A whole day runs from
    /// the start of that day to the start of the next; a partial day maps both times with <see cref="LocalDateTime.InZoneLeniently"/>,
    /// so a range in a DST gap or overlap never throws. A range that collapses to nothing in a gap is left out.
    /// </summary>
    public static IReadOnlyList<Interval> ToIntervals(StaffTimeOff entry, DateTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(zone);

        var intervals = new List<Interval>();
        for (var day = entry.FromDate; day <= entry.ToDate; day = day.PlusDays(1))
        {
            Instant start;
            Instant end;
            if (entry.Range is { } range)
            {
                start = day.At(range.Start).InZoneLeniently(zone).ToInstant();
                end = day.At(range.End).InZoneLeniently(zone).ToInstant();
            }
            else
            {
                start = zone.AtStartOfDay(day).ToInstant();
                end = zone.AtStartOfDay(day.PlusDays(1)).ToInstant();
            }

            if (end > start)
            {
                intervals.Add(new Interval(start, end));
            }
        }

        return intervals;
    }
}

/// <summary>What is wrong with a proposed time-off entry; the slice maps each to a resource key.</summary>
public enum StaffTimeOffProblem
{
    ToBeforeFrom,
    SpanTooLong,
    TimesIncomplete,
    OffStep,
    EndNotAfterStart,
    ReasonTooLong,
}
