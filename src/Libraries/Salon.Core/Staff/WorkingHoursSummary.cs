using NodaTime;

namespace Salon.Core.Staff;

/// <summary>
/// Compresses a weekly rota into the lines a list shows: consecutive days with identical ranges become one group, so
/// "Mon–Fri 09:00–17:00" instead of five lines. Days without ranges are left out. Pure: the view localizes the day names.
/// </summary>
public static class WorkingHoursSummary
{
    public static IReadOnlyList<WorkingHoursGroup> Build(IEnumerable<WorkingHours> hours)
    {
        ArgumentNullException.ThrowIfNull(hours);

        var byDay = hours
            .GroupBy(h => h.Day)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<TimeRange>)g.Select(h => h.Range).OrderBy(r => r.Start).ToArray());

        var groups = new List<WorkingHoursGroup>();
        foreach (var day in WorkingHoursRules.Week)
        {
            if (!byDay.TryGetValue(day, out var ranges) || ranges.Count == 0)
            {
                continue;
            }

            if (groups.Count > 0 && groups[^1] is var last && (int)last.To == (int)day - 1 && last.Ranges.SequenceEqual(ranges))
            {
                groups[^1] = last with { To = day };
            }
            else
            {
                groups.Add(new WorkingHoursGroup(day, day, ranges));
            }
        }

        return groups;
    }
}

/// <summary>Days <see cref="From"/> to <see cref="To"/> (inclusive, consecutive) share the same <see cref="Ranges"/>, sorted by start.</summary>
public sealed record WorkingHoursGroup(IsoDayOfWeek From, IsoDayOfWeek To, IReadOnlyList<TimeRange> Ranges);