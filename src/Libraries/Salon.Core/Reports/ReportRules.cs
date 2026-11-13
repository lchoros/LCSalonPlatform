using NodaTime;
using Salon.Core.Staff;

namespace Salon.Core.Reports;

/// <summary>The periods a report offers with one tap. The names are part of the page address and of the resource keys.</summary>
public enum ReportPreset
{
    ThisWeek = 1,
    LastWeek = 2,
    ThisMonth = 3,
    LastMonth = 4,
}

/// <summary>A span of salon days, both ends included.</summary>
public readonly record struct ReportPeriod(LocalDate From, LocalDate To)
{
    public int Days => Period.DaysBetween(From, To) + 1;
}

/// <summary>
/// What a report asked for and what it got (ADR 0026): <paramref name="Preset"/> is set when the period is one of the
/// one-tap periods, <paramref name="Refused"/> when dates were given that cannot be used and the default stepped in.
/// </summary>
public readonly record struct ResolvedPeriod(ReportPeriod Period, ReportPreset? Preset, bool Refused);

/// <summary>
/// How a report picks its days and turns counts into rates (ADR 0026). Pure. A week starts on Monday in both of the
/// product's markets. A rate without a base is unknown (null), never zero, so an empty week does not read as a bad one.
/// </summary>
public static class ReportRules
{
    /// <summary>A whole year including a leap day; longer spans are asked for in two reports.</summary>
    public const int MaxDays = 366;

    public const ReportPreset DefaultPreset = ReportPreset.ThisMonth;

    public static ReportPeriod Preset(ReportPreset preset, LocalDate today)
    {
        var monday = today.PlusDays(-((int)today.DayOfWeek - (int)IsoDayOfWeek.Monday));
        var firstOfMonth = new LocalDate(today.Year, today.Month, 1, today.Calendar);
        return preset switch
        {
            ReportPreset.ThisWeek => new ReportPeriod(monday, monday.PlusDays(6)),
            ReportPreset.LastWeek => new ReportPeriod(monday.PlusDays(-7), monday.PlusDays(-1)),
            ReportPreset.ThisMonth => new ReportPeriod(firstOfMonth, firstOfMonth.PlusMonths(1).PlusDays(-1)),
            ReportPreset.LastMonth => new ReportPeriod(firstOfMonth.PlusMonths(-1), firstOfMonth.PlusDays(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown report period."),
        };
    }

    /// <summary>True for a span that ends on or after its first day and is no longer than <see cref="MaxDays"/>.</summary>
    public static bool IsUsable(LocalDate from, LocalDate to) => to >= from && Period.DaysBetween(from, to) < MaxDays;

    /// <summary>
    /// The period of a request. Two usable dates win; a preset is next; otherwise <see cref="DefaultPreset"/>. Dates that
    /// were given but cannot be used (half a range, an inverted or too long one) fall back and are reported as refused.
    /// </summary>
    public static ResolvedPeriod Resolve(ReportPreset? preset, LocalDate? from, LocalDate? to, LocalDate today)
    {
        if (from is { } first && to is { } last && IsUsable(first, last))
        {
            var custom = new ReportPeriod(first, last);
            return new ResolvedPeriod(custom, Enum.GetValues<ReportPreset>().Cast<ReportPreset?>().FirstOrDefault(p => Preset(p!.Value, today) == custom), false);
        }

        var chosen = preset is { } known && Enum.IsDefined(known) ? known : DefaultPreset;
        return new ResolvedPeriod(Preset(chosen, today), chosen, from is not null || to is not null);
    }

    /// <summary><paramref name="part"/> as a percentage of <paramref name="whole"/> with one decimal; null without a base.</summary>
    public static decimal? Rate(int part, int whole)
        => whole <= 0 ? null : decimal.Round(100m * Math.Max(0, part) / whole, 1, MidpointRounding.AwayFromZero);

    /// <summary>
    /// Of the visits that should have happened, the share where the client did not turn up. Cancelled visits freed the
    /// time and visits still to come have no outcome, so neither counts.
    /// </summary>
    public static decimal? NoShowRate(int completed, int noShows) => Rate(noShows, completed + noShows);

    /// <summary>
    /// Minutes a person is at work on one day: the rota's ranges less the parts covered by a partial-day absence.
    /// Overlapping input is tolerated; a minute is never counted or taken off twice.
    /// </summary>
    public static int WorkingMinutes(IEnumerable<TimeRange> rota, IEnumerable<TimeRange> timeOff)
    {
        ArgumentNullException.ThrowIfNull(rota);
        ArgumentNullException.ThrowIfNull(timeOff);

        var work = Merge(rota);
        var away = Merge(timeOff);
        var minutes = 0;
        foreach (var (start, end) in work)
        {
            minutes += end - start;
            foreach (var (awayStart, awayEnd) in away)
            {
                minutes -= Math.Max(0, Math.Min(end, awayEnd) - Math.Max(start, awayStart));
            }
        }

        return minutes;
    }

    /// <summary>Booked time as a percentage of working time; null for somebody with no working time in the period.</summary>
    public static decimal? Utilisation(int bookedMinutes, int workingMinutes) => Rate(bookedMinutes, workingMinutes);

    /// <summary>Ranges as minutes of the day, sorted and with overlaps joined; empty and inverted ranges are dropped.</summary>
    private static List<(int Start, int End)> Merge(IEnumerable<TimeRange> ranges)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var (start, end) in ranges
            .Select(r => (Start: MinuteOfDay(r.Start), End: MinuteOfDay(r.End)))
            .Where(r => r.End > r.Start)
            .OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && start <= merged[^1].End)
            {
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, end));
            }
            else
            {
                merged.Add((start, end));
            }
        }

        return merged;
    }

    private static int MinuteOfDay(LocalTime time) => (time.Hour * 60) + time.Minute;
}