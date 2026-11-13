using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using NodaTime;

using Salon.Core.Reports;
using Salon.Core.Staff;

namespace Salon.Tests.Unit.Reports;

/// <summary>How a report picks its days and turns counts into rates (ADR 0026).</summary>
public class ReportRulesTests
{
    /// <summary>A Friday.</summary>
    private static readonly LocalDate Today = new(2026, 11, 13);

    [Fact]
    public void The_one_tap_periods_are_whole_weeks_from_monday_and_whole_months()
    {
        ReportRules.Preset(ReportPreset.ThisWeek, Today).ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 9), new LocalDate(2026, 11, 15)));
        ReportRules.Preset(ReportPreset.LastWeek, Today).ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 8)));
        ReportRules.Preset(ReportPreset.ThisMonth, Today).ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 1), new LocalDate(2026, 11, 30)));
        ReportRules.Preset(ReportPreset.LastMonth, Today).ShouldBe(new ReportPeriod(new LocalDate(2026, 10, 1), new LocalDate(2026, 10, 31)));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(15)]
    public void A_monday_and_a_sunday_belong_to_the_week_they_are_in(int day)
    {
        ReportRules.Preset(ReportPreset.ThisWeek, new LocalDate(2026, 11, day)).ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 9), new LocalDate(2026, 11, 15)));
    }

    [Fact]
    public void Periods_cross_a_new_year_and_know_a_leap_february()
    {
        var newYearsDay = new LocalDate(2027, 1, 1);

        ReportRules.Preset(ReportPreset.LastMonth, newYearsDay).ShouldBe(new ReportPeriod(new LocalDate(2026, 12, 1), new LocalDate(2026, 12, 31)));
        ReportRules.Preset(ReportPreset.ThisWeek, newYearsDay).ShouldBe(new ReportPeriod(new LocalDate(2026, 12, 28), new LocalDate(2027, 1, 3)));
        ReportRules.Preset(ReportPreset.ThisMonth, new LocalDate(2028, 2, 10)).To.ShouldBe(new LocalDate(2028, 2, 29));
        ReportRules.Preset(ReportPreset.LastMonth, new LocalDate(2026, 3, 31)).ShouldBe(new ReportPeriod(new LocalDate(2026, 2, 1), new LocalDate(2026, 2, 28)));
    }

    [Fact]
    public void An_unknown_preset_throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ReportRules.Preset((ReportPreset)99, Today));
    }

    [Property]
    public Property This_week_and_this_month_hold_today_and_the_last_ones_end_the_day_before_they_start(int offset)
    {
        var today = Today.PlusDays(offset % 5000);
        var week = ReportRules.Preset(ReportPreset.ThisWeek, today);
        var month = ReportRules.Preset(ReportPreset.ThisMonth, today);

        return (week.Days == 7
            && week.From.DayOfWeek == IsoDayOfWeek.Monday
            && week.From <= today && today <= week.To
            && month.From.Day == 1 && month.From <= today && today <= month.To
            && month.To.PlusDays(1).Day == 1
            && ReportRules.Preset(ReportPreset.LastWeek, today).To.PlusDays(1) == week.From
            && ReportRules.Preset(ReportPreset.LastWeek, today).Days == 7
            && ReportRules.Preset(ReportPreset.LastMonth, today).To.PlusDays(1) == month.From
            && ReportRules.Preset(ReportPreset.LastMonth, today).From.Day == 1).ToProperty();
    }

    [Fact]
    public void A_span_is_usable_from_one_day_up_to_a_leap_year()
    {
        ReportRules.IsUsable(Today, Today).ShouldBeTrue();
        ReportRules.IsUsable(new LocalDate(2028, 1, 1), new LocalDate(2028, 12, 31)).ShouldBeTrue("366 days");
        ReportRules.IsUsable(new LocalDate(2028, 1, 1), new LocalDate(2029, 1, 1)).ShouldBeFalse("367 days");
        ReportRules.IsUsable(Today, Today.PlusDays(-1)).ShouldBeFalse();
        new ReportPeriod(new LocalDate(2028, 1, 1), new LocalDate(2028, 12, 31)).Days.ShouldBe(ReportRules.MaxDays);
    }

    [Fact]
    public void Two_usable_dates_win_over_a_preset_and_are_recognised_when_they_are_one()
    {
        var custom = ReportRules.Resolve(ReportPreset.LastWeek, new LocalDate(2026, 11, 3), new LocalDate(2026, 11, 12), Today);
        custom.ShouldBe(new ResolvedPeriod(new ReportPeriod(new LocalDate(2026, 11, 3), new LocalDate(2026, 11, 12)), null, false));

        var october = ReportRules.Resolve(null, new LocalDate(2026, 10, 1), new LocalDate(2026, 10, 31), Today);
        october.Preset.ShouldBe(ReportPreset.LastMonth);
        october.Refused.ShouldBeFalse();
    }

    [Fact]
    public void Without_dates_the_preset_is_used_and_without_either_this_month()
    {
        ReportRules.Resolve(ReportPreset.LastWeek, null, null, Today)
            .ShouldBe(new ResolvedPeriod(ReportRules.Preset(ReportPreset.LastWeek, Today), ReportPreset.LastWeek, false));
        ReportRules.Resolve(null, null, null, Today)
            .ShouldBe(new ResolvedPeriod(ReportRules.Preset(ReportPreset.ThisMonth, Today), ReportPreset.ThisMonth, false));
        ReportRules.Resolve((ReportPreset)99, null, null, Today).Preset.ShouldBe(ReportPreset.ThisMonth);
    }

    [Fact]
    public void Dates_that_cannot_be_used_fall_back_and_say_so()
    {
        var half = ReportRules.Resolve(null, Today, null, Today);
        half.ShouldBe(new ResolvedPeriod(ReportRules.Preset(ReportPreset.ThisMonth, Today), ReportPreset.ThisMonth, true));

        var inverted = ReportRules.Resolve(ReportPreset.ThisWeek, Today, Today.PlusDays(-3), Today);
        inverted.ShouldBe(new ResolvedPeriod(ReportRules.Preset(ReportPreset.ThisWeek, Today), ReportPreset.ThisWeek, true));

        ReportRules.Resolve(null, Today.PlusYears(-2), Today, Today).Refused.ShouldBeTrue("two years is too long");
    }

    [Theory]
    [InlineData(1, 4, 25.0)]
    [InlineData(1, 3, 33.3)]
    [InlineData(2, 3, 66.7)]
    [InlineData(1, 8, 12.5)]
    [InlineData(0, 5, 0.0)]
    [InlineData(-2, 5, 0.0)]
    [InlineData(6, 4, 150.0)]
    public void A_rate_is_a_percentage_with_one_decimal(int part, int whole, double expected)
    {
        ReportRules.Rate(part, whole).ShouldBe((decimal)expected);
    }

    [Fact]
    public void A_rate_without_a_base_is_unknown_not_zero()
    {
        ReportRules.Rate(3, 0).ShouldBeNull();
        ReportRules.NoShowRate(0, 0).ShouldBeNull();
        ReportRules.Utilisation(60, 0).ShouldBeNull();
    }

    [Fact]
    public void The_no_show_rate_counts_only_visits_that_had_an_outcome()
    {
        ReportRules.NoShowRate(completed: 9, noShows: 1).ShouldBe(10m);
        ReportRules.NoShowRate(completed: 0, noShows: 2).ShouldBe(100m);
        ReportRules.NoShowRate(completed: 5, noShows: 0).ShouldBe(0m);
    }

    [Fact]
    public void Working_minutes_are_the_rota_less_the_part_somebody_is_away()
    {
        TimeRange[] day = [Range(9, 17)];

        ReportRules.WorkingMinutes(day, []).ShouldBe(480);
        ReportRules.WorkingMinutes([Range(9, 13), Range(14, 18)], []).ShouldBe(480, "a split shift");
        ReportRules.WorkingMinutes(day, [Range(12, 14)]).ShouldBe(360);
        ReportRules.WorkingMinutes(day, [Range(8, 18)]).ShouldBe(0);
        ReportRules.WorkingMinutes(day, [Range(6, 8), Range(17, 19)]).ShouldBe(480, "away outside working hours");
        ReportRules.WorkingMinutes(day, [Range(16, 20)]).ShouldBe(420);
        ReportRules.WorkingMinutes([], [Range(9, 17)]).ShouldBe(0);
    }

    [Fact]
    public void Overlapping_input_never_counts_a_minute_twice()
    {
        ReportRules.WorkingMinutes([Range(9, 13), Range(12, 17)], []).ShouldBe(480);
        ReportRules.WorkingMinutes([Range(9, 17)], [Range(10, 12), Range(11, 13)]).ShouldBe(300);
        ReportRules.WorkingMinutes([new TimeRange(new LocalTime(17, 0), new LocalTime(9, 0))], []).ShouldBe(0, "an inverted range is dropped");
    }

    [Property]
    public Property Working_minutes_stay_between_nothing_and_the_rota(byte startHour, byte length, byte awayStart, byte awayLength)
    {
        var start = startHour % 12;
        var end = start + 1 + (length % 11);
        var from = awayStart % 20;
        var until = from + 1 + (awayLength % 3);

        var minutes = ReportRules.WorkingMinutes([Range(start, end)], [Range(from, until)]);

        return (minutes >= 0 && minutes <= (end - start) * 60).ToProperty();
    }

    [Fact]
    public void Utilisation_is_booked_time_over_working_time_and_may_pass_a_hundred()
    {
        ReportRules.Utilisation(240, 480).ShouldBe(50m);
        ReportRules.Utilisation(0, 480).ShouldBe(0m);
        ReportRules.Utilisation(600, 480).ShouldBe(125m, "booked outside the rota");
    }

    private static TimeRange Range(int fromHour, int toHour) => new(new LocalTime(fromHour, 0), new LocalTime(toHour, 0));
}