using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Notifications;
using Salon.Core.Settings;

namespace Salon.Tests.Unit.Notifications;

/// <summary>
/// ADR 0050: the daily summary is due from its minute until midnight on the salon's clock, when switched on, and a
/// time of day is read and written as a form shows it.
/// </summary>
public class DailySummaryRulesTests
{
    private static readonly LocalDate Day = new(2026, 11, 16);

    [Theory]
    [InlineData(19, 59, false)]
    [InlineData(20, 0, true)]
    [InlineData(20, 4, true)]
    [InlineData(23, 59, true)]
    [InlineData(0, 0, false)]
    public void It_is_due_from_its_minute_until_midnight(int hour, int minute, bool due)
    {
        var settings = new DailySummarySettings { Enabled = true, SendAtMinute = 20 * 60 };

        DailySummaryRules.IsDue(settings, Day + new LocalTime(hour, minute)).ShouldBe(due);
    }

    [Fact]
    public void It_is_off_until_switched_on_and_a_time_outside_the_day_never_comes()
    {
        var evening = Day + new LocalTime(21, 0);

        new DailySummarySettings().Enabled.ShouldBeFalse("off by default");
        DailySummaryRules.IsDue(new DailySummarySettings(), evening).ShouldBeFalse();
        DailySummaryRules.IsDue(new DailySummarySettings { Enabled = true, SendAtMinute = 1440 }, evening).ShouldBeFalse();
        DailySummaryRules.IsDue(new DailySummarySettings { Enabled = true, SendAtMinute = -1 }, evening).ShouldBeFalse();
        DailySummaryRules.IsDue(new DailySummarySettings { Enabled = true, SendAtMinute = 0 }, Day + LocalTime.Midnight).ShouldBeTrue("midnight is the first minute of the day");
    }

    [Theory]
    [InlineData("20:00", 1200)]
    [InlineData("07:30", 450)]
    [InlineData("7:30", 450)]
    [InlineData("00:00", 0)]
    [InlineData("23:59", 1439)]
    [InlineData(" 08:05 ", 485)]
    public void A_time_of_day_is_read_as_a_form_writes_it(string text, int minute)
    {
        DailySummaryRules.ParseClock(text).ShouldBe(minute);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("20:60")]
    [InlineData("20")]
    [InlineData("20:0")]
    [InlineData("8pm")]
    [InlineData("-1:00")]
    [InlineData("20:00:00")]
    [InlineData("+8:00")]
    public void Anything_else_is_not_a_time(string? text)
    {
        DailySummaryRules.ParseClock(text).ShouldBeNull();
    }

    [Fact]
    public void A_minute_of_the_day_is_written_with_two_digits_each()
    {
        DailySummaryRules.ToClock(1200).ShouldBe("20:00");
        DailySummaryRules.ToClock(5).ShouldBe("00:05");
        DailySummaryRules.ToClock(1440).ShouldBeEmpty();
    }

    [Property]
    public Property Writing_a_minute_and_reading_it_back_gives_the_same_minute(ushort any)
    {
        var minute = any % DailySummarySettings.MinutesPerDay;

        return (DailySummaryRules.ParseClock(DailySummaryRules.ToClock(minute)) == minute).ToProperty();
    }
}