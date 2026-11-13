using NodaTime;
using Salon.Core.Staff;

namespace Salon.Services.Appointments;

/// <summary>
/// The pure geometry of the server-rendered admin calendar (ADR 0016): a timetable in 15-minute rows from the earliest
/// working start to the latest working end (08:00–20:00 when nobody has hours), each appointment as a block placed by
/// its start and duration, and side-by-side lanes when two blocks of one column overlap. Minutes of the local day are
/// used throughout so a range that ends at midnight (1440) needs no special case; the views turn rows into CSS grid lines.
/// </summary>
public static class CalendarLayout
{
    public const int StepMinutes = 15;
    public const int MinutesPerDay = 24 * 60;
    public const int DefaultStartMinute = 8 * 60;
    public const int DefaultEndMinute = 20 * 60;

    /// <summary>
    /// The rows to draw: the earliest start and latest end of <paramref name="working"/>, each widened to the step, or
    /// the 08:00–20:00 fallback when there are none; always widened further so every one of <paramref name="blocks"/>
    /// is inside (a visit booked outside the rota must still be visible).
    /// </summary>
    public static GridRange Range(IEnumerable<TimeRange> working, IEnumerable<BlockInput> blocks)
    {
        ArgumentNullException.ThrowIfNull(working);
        ArgumentNullException.ThrowIfNull(blocks);

        var start = int.MaxValue;
        var end = int.MinValue;
        foreach (var range in working)
        {
            start = Math.Min(start, MinuteOf(range.Start));
            end = Math.Max(end, MinuteOf(range.End));
        }

        if (start == int.MaxValue)
        {
            (start, end) = (DefaultStartMinute, DefaultEndMinute);
        }

        foreach (var block in blocks)
        {
            start = Math.Min(start, block.StartMinute);
            end = Math.Max(end, block.EndMinute);
        }

        start = Math.Clamp(FloorToStep(start), 0, MinutesPerDay - StepMinutes);
        end = Math.Clamp(CeilToStep(end), start + StepMinutes, MinutesPerDay);
        return new GridRange(start, end);
    }

    /// <summary>The first minute of row <paramref name="row"/> (1-based).</summary>
    public static int MinuteOfRow(GridRange grid, int row) => grid.StartMinute + ((row - 1) * StepMinutes);

    /// <summary>True when the 15 minutes starting at <paramref name="minute"/> lie inside one of the working ranges.</summary>
    public static bool IsWorking(IEnumerable<TimeRange> working, int minute)
    {
        ArgumentNullException.ThrowIfNull(working);
        return working.Any(r => MinuteOf(r.Start) <= minute && minute + StepMinutes <= MinuteOf(r.End));
    }

    /// <summary>
    /// Places every block on the grid: the 1-based row of its start, the rows it spans (at least one, cut at the grid
    /// end) and, when blocks of the same column overlap, the lane each takes and how many lanes its cluster needs. Blocks
    /// that merely touch (10:00–10:30, 10:30–11:00) share no cluster. Output is in start order.
    /// </summary>
    public static IReadOnlyList<PositionedBlock> Position(GridRange grid, IReadOnlyList<BlockInput> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var ordered = blocks.OrderBy(b => b.StartMinute).ThenByDescending(b => b.EndMinute).ThenBy(b => b.Id).ToList();
        var result = new List<PositionedBlock>(ordered.Count);
        var cluster = new List<(BlockInput Block, int Lane)>();
        var laneEnds = new List<int>();
        var clusterEnd = int.MinValue;

        foreach (var block in ordered)
        {
            if (block.StartMinute >= clusterEnd && cluster.Count > 0)
            {
                Flush();
            }

            var lane = laneEnds.FindIndex(end => end <= block.StartMinute);
            if (lane < 0)
            {
                lane = laneEnds.Count;
                laneEnds.Add(block.EndMinute);
            }
            else
            {
                laneEnds[lane] = block.EndMinute;
            }

            cluster.Add((block, lane));
            clusterEnd = Math.Max(clusterEnd, block.EndMinute);
        }

        Flush();
        return result;

        void Flush()
        {
            var lanes = Math.Max(1, laneEnds.Count);
            foreach (var (block, lane) in cluster)
            {
                result.Add(new PositionedBlock(block.Id, RowOf(grid, block.StartMinute), SpanOf(grid, block.StartMinute, block.EndMinute), lane, lanes));
            }

            cluster.Clear();
            laneEnds.Clear();
            clusterEnd = int.MinValue;
        }
    }

    /// <summary>The 1-based row that holds <paramref name="minute"/>, clamped to the grid.</summary>
    public static int RowOf(GridRange grid, int minute)
        => Math.Clamp(((Math.Max(minute, grid.StartMinute) - grid.StartMinute) / StepMinutes) + 1, 1, grid.RowCount);

    /// <summary>How many rows a block from <paramref name="startMinute"/> to <paramref name="endMinute"/> covers; at least one, never past the grid end.</summary>
    public static int SpanOf(GridRange grid, int startMinute, int endMinute)
    {
        var row = RowOf(grid, startMinute);
        var lastRow = Math.Clamp(CeilToStep(Math.Min(endMinute, grid.EndMinute) - grid.StartMinute) / StepMinutes, row, grid.RowCount);
        return Math.Max(1, lastRow - row + 1);
    }

    /// <summary>Minute of the day of a local time (seconds dropped).</summary>
    public static int MinuteOf(LocalTime time) => (time.Hour * 60) + time.Minute;

    private static int FloorToStep(int minute) => minute / StepMinutes * StepMinutes;

    private static int CeilToStep(int minute) => (minute + StepMinutes - 1) / StepMinutes * StepMinutes;
}

/// <summary>The rows of the timetable: from <see cref="StartMinute"/> (inclusive) to <see cref="EndMinute"/> (exclusive, up to 1440), both on the step.</summary>
public readonly record struct GridRange(int StartMinute, int EndMinute)
{
    public int RowCount => (EndMinute - StartMinute) / CalendarLayout.StepMinutes;

    public LocalTime Start => LocalTime.FromMinutesSinceMidnight(StartMinute);
}

/// <summary>One appointment to place, in minutes of its local day; <see cref="EndMinute"/> may be 1440 for a visit that runs to midnight.</summary>
public sealed record BlockInput(Guid Id, int StartMinute, int EndMinute);

/// <param name="Row">1-based grid row of the start.</param>
/// <param name="Span">Rows covered; at least one.</param>
/// <param name="Lane">0-based lane inside the overlap cluster; 0 when nothing overlaps.</param>
/// <param name="Lanes">Lanes the cluster needs; 1 when nothing overlaps.</param>
public sealed record PositionedBlock(Guid Id, int Row, int Span, int Lane, int Lanes);