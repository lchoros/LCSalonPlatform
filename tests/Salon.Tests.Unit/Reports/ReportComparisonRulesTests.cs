using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Reports;

namespace Salon.Tests.Unit.Reports;

/// <summary>
/// ADR 0046: a period is compared with the same days one period earlier, and only with the days that have happened,
/// so a week or a month that is not over is compared like for like.
/// </summary>
public class ReportComparisonRulesTests
{
    /// <summary>A Friday.</summary>
    private static readonly LocalDate Today = new(2026, 11, 13);

    [Fact]
    public void A_week_in_progress_is_compared_with_the_same_weekdays_of_the_week_before()
    {
        var week = ReportRules.Preset(ReportPreset.ThisWeek, Today);

        var compared = ReportRules.Compare(week, Today).ShouldNotBeNull();

        compared.SoFar.ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 9), Today), "Monday to today");
        compared.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 6)), "Monday to Friday a week earlier, so a Saturday is never set against a Tuesday");
    }

    [Fact]
    public void A_week_that_is_over_is_compared_with_the_whole_week_before()
    {
        var compared = ReportRules.Compare(ReportRules.Preset(ReportPreset.LastWeek, Today), Today).ShouldNotBeNull();

        compared.SoFar.ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 2), new LocalDate(2026, 11, 8)));
        compared.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 10, 26), new LocalDate(2026, 11, 1)));
    }

    [Fact]
    public void A_month_in_progress_is_compared_with_the_same_days_of_the_month_before()
    {
        var compared = ReportRules.Compare(ReportRules.Preset(ReportPreset.ThisMonth, Today), Today).ShouldNotBeNull();

        compared.SoFar.ShouldBe(new ReportPeriod(new LocalDate(2026, 11, 1), Today));
        compared.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 10, 1), new LocalDate(2026, 10, 13)));
    }

    [Fact]
    public void A_whole_month_is_compared_with_the_whole_month_before_whatever_their_lengths()
    {
        var october = ReportRules.Compare(ReportRules.Preset(ReportPreset.LastMonth, Today), Today).ShouldNotBeNull();
        var march = ReportRules.Compare(new ReportPeriod(new LocalDate(2026, 3, 1), new LocalDate(2026, 3, 31)), Today).ShouldNotBeNull();

        october.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 9, 1), new LocalDate(2026, 9, 30)));
        march.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 2, 1), new LocalDate(2026, 2, 28)), "31 days against 28");
    }

    [Fact]
    public void The_thirtieth_of_march_has_no_thirtieth_of_february_and_takes_the_whole_of_it()
    {
        var march = new ReportPeriod(new LocalDate(2026, 3, 1), new LocalDate(2026, 3, 31));

        var compared = ReportRules.Compare(march, new LocalDate(2026, 3, 30)).ShouldNotBeNull();

        compared.SoFar.ShouldBe(new ReportPeriod(new LocalDate(2026, 3, 1), new LocalDate(2026, 3, 30)));
        compared.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 2, 1), new LocalDate(2026, 2, 28)));
    }

    [Fact]
    public void Any_other_dates_are_compared_with_as_many_days_just_before_them()
    {
        var tenDays = new ReportPeriod(new LocalDate(2026, 10, 11), new LocalDate(2026, 10, 20));
        var midMonth = new ReportPeriod(new LocalDate(2026, 10, 15), new LocalDate(2026, 11, 14));

        ReportRules.Compare(tenDays, Today).ShouldNotBeNull().Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 10, 1), new LocalDate(2026, 10, 10)));
        var running = ReportRules.Compare(midMonth, Today).ShouldNotBeNull();
        running.SoFar.ShouldBe(new ReportPeriod(new LocalDate(2026, 10, 15), Today));
        running.Before.ShouldBe(new ReportPeriod(new LocalDate(2026, 9, 14), new LocalDate(2026, 10, 13)), "31 days earlier, cut at the same day of the span");
    }

    [Fact]
    public void A_period_that_begins_today_is_one_day_and_one_that_has_not_begun_has_nothing_to_compare()
    {
        var fromToday = new ReportPeriod(Today, Today.PlusDays(6));
        var nextWeek = new ReportPeriod(Today.PlusDays(1), Today.PlusDays(7));

        var compared = ReportRules.Compare(fromToday, Today).ShouldNotBeNull();
        compared.SoFar.ShouldBe(new ReportPeriod(Today, Today));
        compared.Before.ShouldBe(new ReportPeriod(Today.PlusDays(-7), Today.PlusDays(-7)));
        ReportRules.Compare(nextWeek, Today).ShouldBeNull();
    }

    [Property]
    public Property The_days_before_always_end_before_the_period_begins_and_the_days_so_far_stay_inside_it(byte startOffset, byte length, byte todayOffset)
    {
        var from = new LocalDate(2026, 1, 1).PlusDays(startOffset);
        var period = new ReportPeriod(from, from.PlusDays(length % 120));
        var today = from.PlusDays(todayOffset % 150);

        var compared = ReportRules.Compare(period, today);

        return (compared is { } c
            && c.Before.To < period.From
            && c.Before.From <= c.Before.To
            && c.SoFar.From == period.From
            && c.SoFar.To <= period.To
            && c.SoFar.To <= today).ToProperty();
    }

    [Theory]
    [InlineData(81, 60, 35.0)]
    [InlineData(60, 81, -25.9)]
    [InlineData(20, 60, -66.7)]
    [InlineData(10, 10, 0.0)]
    [InlineData(0, 10, -100.0)]
    [InlineData(30, 10, 200.0)]
    public void A_change_is_how_much_more_or_less_than_before_as_a_percentage_with_one_decimal(double now, double before, double expected)
    {
        ReportRules.Change((decimal)now, (decimal)before).ShouldBe((decimal)expected);
    }

    [Fact]
    public void A_change_from_nothing_is_unknown_not_endless()
    {
        ReportRules.Change(50m, 0m).ShouldBeNull();
        ReportRules.Change(0m, 0m).ShouldBeNull();
        ReportRules.Change(50m, -5m).ShouldBeNull("more went out than came in before; a percentage of that says nothing");
    }
}