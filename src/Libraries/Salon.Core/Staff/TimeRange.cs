using NodaTime;

namespace Salon.Core.Staff;

/// <summary>A half-open range of local time on one day: <see cref="Start"/> inclusive, <see cref="End"/> exclusive.</summary>
public readonly record struct TimeRange(LocalTime Start, LocalTime End)
{
    /// <summary>True when the two ranges share at least one instant; ranges that merely touch (09:00–13:00, 13:00–17:00) do not.</summary>
    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}