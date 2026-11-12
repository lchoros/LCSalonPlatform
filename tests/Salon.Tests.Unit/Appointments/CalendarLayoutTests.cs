using NodaTime;

using Salon.Core.Staff;
using Salon.Services.Appointments;

namespace Salon.Tests.Unit.Appointments;

/// <summary>The pure geometry of the admin calendar (ADR 0016): grid rows from the rota, block rows and spans, overlap lanes.</summary>
public class CalendarLayoutTests
{
    private static readonly Guid A = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-00000000000a");
    private static readonly Guid B = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-00000000000b");
    private static readonly Guid C = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-00000000000c");

    [Fact]
    public void The_grid_runs_from_the_earliest_working_start_to_the_latest_end_in_15_minute_rows()
    {
        var grid = CalendarLayout.Range([Range("10:00", "18:00"), Range("09:00", "13:00"), Range("14:00", "17:00")], []);

        grid.StartMinute.ShouldBe(9 * 60);
        grid.EndMinute.ShouldBe(18 * 60);
        grid.RowCount.ShouldBe(36);
        grid.Start.ShouldBe(new LocalTime(9, 0));
        CalendarLayout.MinuteOfRow(grid, 1).ShouldBe(9 * 60);
        CalendarLayout.MinuteOfRow(grid, 5).ShouldBe(10 * 60);
    }

    [Fact]
    public void Without_working_hours_the_grid_falls_back_to_eight_to_twenty()
    {
        var grid = CalendarLayout.Range([], []);

        grid.ShouldBe(new GridRange(8 * 60, 20 * 60));
        grid.RowCount.ShouldBe(48);
    }

    [Fact]
    public void Off_grid_hours_are_widened_to_the_step_and_a_visit_outside_the_rota_stretches_the_grid()
    {
        var grid = CalendarLayout.Range([Range("09:05", "16:50")], [new BlockInput(A, 7 * 60 + 30, 8 * 60), new BlockInput(B, 17 * 60, 18 * 60 + 10)]);

        grid.StartMinute.ShouldBe(7 * 60 + 30);
        grid.EndMinute.ShouldBe(18 * 60 + 15);
    }

    [Fact]
    public void A_rota_that_ends_at_midnight_fits_without_overflowing_the_day()
    {
        var grid = CalendarLayout.Range([Range("20:00", "23:55")], []);

        grid.EndMinute.ShouldBe(CalendarLayout.MinutesPerDay);
        grid.RowCount.ShouldBe(16);
    }

    [Fact]
    public void A_row_is_working_only_when_its_whole_quarter_hour_is_inside_a_range()
    {
        TimeRange[] ranges = [Range("09:00", "13:00"), Range("14:00", "17:00")];

        CalendarLayout.IsWorking(ranges, 9 * 60).ShouldBeTrue();
        CalendarLayout.IsWorking(ranges, 12 * 60 + 45).ShouldBeTrue();
        CalendarLayout.IsWorking(ranges, 13 * 60).ShouldBeFalse();
        CalendarLayout.IsWorking(ranges, 13 * 60 + 45).ShouldBeFalse();
        CalendarLayout.IsWorking(ranges, 14 * 60).ShouldBeTrue();
        CalendarLayout.IsWorking(ranges, 16 * 60 + 45).ShouldBeTrue();
        CalendarLayout.IsWorking(ranges, 17 * 60).ShouldBeFalse();
        CalendarLayout.IsWorking([], 10 * 60).ShouldBeFalse();
    }

    [Fact]
    public void A_block_takes_the_row_of_its_start_and_spans_its_duration_rounded_up_to_whole_rows()
    {
        var grid = new GridRange(9 * 60, 17 * 60);

        var blocks = CalendarLayout.Position(grid, [new BlockInput(A, 10 * 60, 10 * 60 + 45), new BlockInput(B, 11 * 60, 11 * 60 + 20)]);

        blocks.Select(b => b.Id).ShouldBe([A, B]);
        blocks[0].ShouldBe(new PositionedBlock(A, 5, 3, 0, 1));
        blocks[1].Row.ShouldBe(9);
        blocks[1].Span.ShouldBe(2, "20 minutes still needs two 15-minute rows");
        blocks[1].Lanes.ShouldBe(1);
    }

    [Fact]
    public void A_block_past_the_grid_end_is_cut_and_a_tiny_one_still_gets_a_row()
    {
        var grid = new GridRange(9 * 60, 12 * 60);

        var blocks = CalendarLayout.Position(grid, [new BlockInput(A, 11 * 60 + 30, 13 * 60), new BlockInput(B, 9 * 60, 9 * 60 + 5)]);

        blocks[0].Id.ShouldBe(B);
        blocks[0].Row.ShouldBe(1);
        blocks[0].Span.ShouldBe(1);
        blocks[1].Row.ShouldBe(11);
        blocks[1].Span.ShouldBe(2, "11:30–13:00 is cut at the 12:00 grid end");
        (blocks[1].Row + blocks[1].Span - 1).ShouldBeLessThanOrEqualTo(grid.RowCount);
    }

    [Fact]
    public void Overlapping_blocks_of_one_column_share_lanes_and_touching_ones_do_not()
    {
        var grid = new GridRange(9 * 60, 17 * 60);
        BlockInput[] input =
        [
            new(A, 10 * 60, 11 * 60),
            new(B, 10 * 60 + 30, 11 * 60 + 30),
            new(C, 11 * 60 + 30, 12 * 60),
        ];

        var blocks = CalendarLayout.Position(grid, input);

        var a = blocks.Single(b => b.Id == A);
        var b = blocks.Single(b => b.Id == B);
        var c = blocks.Single(b => b.Id == C);
        a.Lane.ShouldBe(0);
        a.Lanes.ShouldBe(2);
        b.Lane.ShouldBe(1);
        b.Lanes.ShouldBe(2);
        c.Lane.ShouldBe(0, "11:30 starts when B ends, so it is a new cluster");
        c.Lanes.ShouldBe(1);
    }

    [Fact]
    public void Three_way_overlaps_need_three_lanes_and_a_freed_lane_is_reused()
    {
        var grid = new GridRange(9 * 60, 17 * 60);
        BlockInput[] input =
        [
            new(A, 10 * 60, 12 * 60),
            new(B, 10 * 60 + 15, 10 * 60 + 45),
            new(C, 10 * 60 + 30, 11 * 60),
            new(Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-00000000000d"), 11 * 60, 11 * 60 + 30),
        ];

        var blocks = CalendarLayout.Position(grid, input);

        blocks.ShouldAllBe(b => b.Lanes == 3);
        blocks.Select(b => b.Lane).ShouldBe([0, 1, 2, 1], "the fourth block reuses lane 1 once B has ended");
    }

    [Fact]
    public void Positioning_nothing_gives_nothing()
    {
        CalendarLayout.Position(new GridRange(8 * 60, 20 * 60), []).ShouldBeEmpty();
    }

    private static TimeRange Range(string start, string end)
        => new(WorkingHoursRules.TryParseTime(start)!.Value, WorkingHoursRules.TryParseTime(end)!.Value);
}