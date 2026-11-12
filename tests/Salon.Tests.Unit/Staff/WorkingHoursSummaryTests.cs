using NodaTime;

using Salon.Core.Staff;

namespace Salon.Tests.Unit.Staff;

/// <summary>Consecutive days with the same ranges collapse to one group; everything else stays apart (ADR 0010).</summary>
public class WorkingHoursSummaryTests
{
    private static readonly Guid Staff = Guid.CreateVersion7();

    [Fact]
    public void No_rows_means_no_groups()
    {
        WorkingHoursSummary.Build([]).ShouldBeEmpty();
    }

    [Fact]
    public void Five_identical_weekdays_become_Monday_to_Friday()
    {
        var hours = Weekdays(IsoDayOfWeek.Monday, IsoDayOfWeek.Friday, (9, 0, 17, 0));

        var groups = WorkingHoursSummary.Build(hours);

        var group = groups.ShouldHaveSingleItem();
        group.From.ShouldBe(IsoDayOfWeek.Monday);
        group.To.ShouldBe(IsoDayOfWeek.Friday);
        group.Ranges.ShouldBe([Range(9, 0, 17, 0)]);
    }

    [Fact]
    public void A_split_shift_groups_as_long_as_both_ranges_match()
    {
        var hours = Weekdays(IsoDayOfWeek.Monday, IsoDayOfWeek.Friday, (14, 0, 17, 0), (9, 0, 13, 0));

        var group = WorkingHoursSummary.Build(hours).ShouldHaveSingleItem();

        group.From.ShouldBe(IsoDayOfWeek.Monday);
        group.To.ShouldBe(IsoDayOfWeek.Friday);
        group.Ranges.ShouldBe([Range(9, 0, 13, 0), Range(14, 0, 17, 0)], "ranges are sorted by start regardless of row order");
    }

    [Fact]
    public void Alternate_days_stay_separate()
    {
        var hours = new[] { Row(IsoDayOfWeek.Monday, 9, 0, 15, 0), Row(IsoDayOfWeek.Wednesday, 9, 0, 15, 0), Row(IsoDayOfWeek.Friday, 9, 0, 15, 0) };

        var groups = WorkingHoursSummary.Build(hours);

        groups.Select(g => (g.From, g.To)).ShouldBe(
        [
            (IsoDayOfWeek.Monday, IsoDayOfWeek.Monday),
            (IsoDayOfWeek.Wednesday, IsoDayOfWeek.Wednesday),
            (IsoDayOfWeek.Friday, IsoDayOfWeek.Friday),
        ]);
        groups.ShouldAllBe(g => g.Ranges.Count == 1 && g.Ranges[0] == Range(9, 0, 15, 0));
    }

    [Fact]
    public void A_day_with_different_hours_breaks_the_group()
    {
        var hours = Weekdays(IsoDayOfWeek.Tuesday, IsoDayOfWeek.Friday, (10, 0, 18, 0)).Append(Row(IsoDayOfWeek.Saturday, 10, 0, 14, 0));

        var groups = WorkingHoursSummary.Build(hours);

        groups.Count.ShouldBe(2);
        groups[0].From.ShouldBe(IsoDayOfWeek.Tuesday);
        groups[0].To.ShouldBe(IsoDayOfWeek.Friday);
        groups[0].Ranges.ShouldBe([Range(10, 0, 18, 0)]);
        groups[1].From.ShouldBe(IsoDayOfWeek.Saturday);
        groups[1].To.ShouldBe(IsoDayOfWeek.Saturday);
        groups[1].Ranges.ShouldBe([Range(10, 0, 14, 0)]);
    }

    [Fact]
    public void Groups_come_in_week_order_whatever_the_row_order_and_never_wrap_past_Sunday()
    {
        var hours = new[] { Row(IsoDayOfWeek.Sunday, 10, 0, 16, 0), Row(IsoDayOfWeek.Monday, 10, 0, 16, 0), Row(IsoDayOfWeek.Saturday, 10, 0, 16, 0) };

        var groups = WorkingHoursSummary.Build(hours);

        groups.Select(g => (g.From, g.To)).ShouldBe([(IsoDayOfWeek.Monday, IsoDayOfWeek.Monday), (IsoDayOfWeek.Saturday, IsoDayOfWeek.Sunday)]);
    }

    private static IEnumerable<WorkingHours> Weekdays(IsoDayOfWeek from, IsoDayOfWeek to, params (int FromHour, int FromMinute, int ToHour, int ToMinute)[] ranges)
    {
        for (var day = from; day <= to; day = (IsoDayOfWeek)((int)day + 1))
        {
            foreach (var (fromHour, fromMinute, toHour, toMinute) in ranges)
            {
                yield return Row(day, fromHour, fromMinute, toHour, toMinute);
            }
        }
    }

    private static WorkingHours Row(IsoDayOfWeek day, int fromHour, int fromMinute, int toHour, int toMinute)
        => new() { StaffMemberId = Staff, Day = day, Start = new LocalTime(fromHour, fromMinute), End = new LocalTime(toHour, toMinute) };

    private static TimeRange Range(int fromHour, int fromMinute, int toHour, int toMinute)
        => new(new LocalTime(fromHour, fromMinute), new LocalTime(toHour, toMinute));
}