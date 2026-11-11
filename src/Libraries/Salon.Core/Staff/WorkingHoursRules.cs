using NodaTime;
using NodaTime.Text;

namespace Salon.Core.Staff;

/// <summary>
/// The pure rules a weekly rota must satisfy (ADR 0010). The EditWorkingHours validator turns each failure into a
/// localized message; these methods only decide. Times are on a 5-minute grid, a range ends after it starts on the same
/// day, a day holds at most <see cref="MaxRangesPerDay"/> ranges and none of them overlap; a day without ranges is a day off.
/// </summary>
public static class WorkingHoursRules
{
    public const int StepMinutes = 5;

    /// <summary>Enough for a split shift (morning and afternoon); more ranges per day are an M2 question.</summary>
    public const int MaxRangesPerDay = 2;

    private static readonly LocalTimePattern[] AcceptedPatterns =
    [
        LocalTimePattern.CreateWithInvariantCulture("HH:mm"),
        LocalTimePattern.CreateWithInvariantCulture("HH:mm:ss"),
    ];

    /// <summary>The seven ISO days from Monday to Sunday, the order every rota screen uses.</summary>
    public static IReadOnlyList<IsoDayOfWeek> Week { get; } =
    [
        IsoDayOfWeek.Monday,
        IsoDayOfWeek.Tuesday,
        IsoDayOfWeek.Wednesday,
        IsoDayOfWeek.Thursday,
        IsoDayOfWeek.Friday,
        IsoDayOfWeek.Saturday,
        IsoDayOfWeek.Sunday,
    ];

    /// <summary>Parses what an <c>&lt;input type="time"&gt;</c> posts ("09:00" or "09:00:00"); null for anything else.</summary>
    public static LocalTime? TryParseTime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var pattern in AcceptedPatterns)
        {
            var result = pattern.Parse(text.Trim());
            if (result.Success)
            {
                return result.Value;
            }
        }

        return null;
    }

    /// <summary>True when the time sits on the 5-minute grid with no seconds.</summary>
    public static bool IsOnStep(LocalTime time) => time.Second == 0 && time.NanosecondOfSecond == 0 && time.Minute % StepMinutes == 0;

    /// <summary>True when the range ends strictly after it starts (same day, so midnight cannot be the end).</summary>
    public static bool EndsAfterStart(TimeRange range) => range.End > range.Start;

    /// <summary>Zero-based indexes of the ranges that overlap any other range of the same day; empty when the day is consistent.</summary>
    public static IReadOnlyList<int> OverlappingIndexes(IReadOnlyList<TimeRange> ranges)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var overlapping = new SortedSet<int>();
        for (var i = 0; i < ranges.Count; i++)
        {
            for (var j = i + 1; j < ranges.Count; j++)
            {
                if (ranges[i].Overlaps(ranges[j]))
                {
                    overlapping.Add(i);
                    overlapping.Add(j);
                }
            }
        }

        return [.. overlapping];
    }
}