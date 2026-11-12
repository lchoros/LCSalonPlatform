using NodaTime;

using Salon.Core.Staff;

namespace Salon.Tests.Unit.Staff;

/// <summary>The pure rota rules of ADR 0010: parsing of time-input text, the 5-minute grid, end after start, overlap detection, the day order.</summary>
public class WorkingHoursRulesTests
{
    [Theory]
    [InlineData("09:00", 9, 0)]
    [InlineData("09:05:00", 9, 5)]
    [InlineData(" 17:30 ", 17, 30)]
    [InlineData("00:00", 0, 0)]
    [InlineData("23:55", 23, 55)]
    public void Parses_what_a_time_input_posts(string text, int hour, int minute)
    {
        WorkingHoursRules.TryParseTime(text).ShouldBe(new LocalTime(hour, minute));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("9am")]
    [InlineData("24:00")]
    [InlineData("09:60")]
    [InlineData("9:00")]
    [InlineData("09.00")]
    public void Anything_else_does_not_parse(string? text)
    {
        WorkingHoursRules.TryParseTime(text).ShouldBeNull();
    }

    [Theory]
    [InlineData(9, 0, true)]
    [InlineData(9, 5, true)]
    [InlineData(9, 55, true)]
    [InlineData(9, 3, false)]
    [InlineData(9, 1, false)]
    public void Times_sit_on_a_five_minute_grid(int hour, int minute, bool onStep)
    {
        WorkingHoursRules.IsOnStep(new LocalTime(hour, minute)).ShouldBe(onStep);
    }

    [Fact]
    public void Seconds_take_a_time_off_the_grid()
    {
        WorkingHoursRules.IsOnStep(new LocalTime(9, 0, 30)).ShouldBeFalse();
        WorkingHoursRules.IsOnStep(LocalTime.FromHourMinuteSecondNanosecond(9, 0, 0, 1)).ShouldBeFalse();
    }

    [Fact]
    public void A_range_must_end_after_it_starts()
    {
        WorkingHoursRules.EndsAfterStart(Range(9, 0, 9, 5)).ShouldBeTrue();
        WorkingHoursRules.EndsAfterStart(Range(9, 0, 9, 0)).ShouldBeFalse();
        WorkingHoursRules.EndsAfterStart(Range(17, 0, 9, 0)).ShouldBeFalse();
        // Midnight is the start of the day, so an "until midnight" shift cannot be expressed in M1.
        WorkingHoursRules.EndsAfterStart(Range(22, 0, 0, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Ranges_that_touch_do_not_overlap()
    {
        WorkingHoursRules.OverlappingIndexes([Range(9, 0, 13, 0), Range(13, 0, 17, 0)]).ShouldBeEmpty();
        Range(9, 0, 13, 0).Overlaps(Range(13, 0, 17, 0)).ShouldBeFalse();
    }

    [Fact]
    public void Ranges_that_share_a_minute_overlap_in_either_order()
    {
        WorkingHoursRules.OverlappingIndexes([Range(9, 0, 13, 5), Range(13, 0, 17, 0)]).ShouldBe([0, 1]);
        WorkingHoursRules.OverlappingIndexes([Range(13, 0, 17, 0), Range(9, 0, 13, 5)]).ShouldBe([0, 1]);
        Range(9, 0, 13, 5).Overlaps(Range(13, 0, 17, 0)).ShouldBeTrue();
    }

    [Fact]
    public void A_range_inside_another_overlaps_it()
    {
        WorkingHoursRules.OverlappingIndexes([Range(9, 0, 17, 0), Range(12, 0, 13, 0)]).ShouldBe([0, 1]);
    }

    [Fact]
    public void Only_the_offending_ranges_are_reported()
    {
        var ranges = new[] { Range(8, 0, 9, 0), Range(10, 0, 12, 0), Range(11, 0, 13, 0) };

        WorkingHoursRules.OverlappingIndexes(ranges).ShouldBe([1, 2]);
    }

    [Fact]
    public void An_empty_or_single_range_day_has_no_overlap()
    {
        WorkingHoursRules.OverlappingIndexes([]).ShouldBeEmpty();
        WorkingHoursRules.OverlappingIndexes([Range(9, 0, 17, 0)]).ShouldBeEmpty();
    }

    [Fact]
    public void The_week_runs_Monday_to_Sunday_and_the_limits_are_as_decided()
    {
        WorkingHoursRules.Week.ShouldBe(
        [
            IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday,
            IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday,
        ]);
        WorkingHoursRules.StepMinutes.ShouldBe(5);
        WorkingHoursRules.MaxRangesPerDay.ShouldBe(2);
    }

    private static TimeRange Range(int fromHour, int fromMinute, int toHour, int toMinute)
        => new(new LocalTime(fromHour, fromMinute), new LocalTime(toHour, toMinute));
}