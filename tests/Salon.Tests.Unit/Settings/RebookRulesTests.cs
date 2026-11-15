using NodaTime;
using Salon.Core.Settings;

namespace Salon.Tests.Unit.Settings;

/// <summary>The one-tap choices for a client's next visit (ADR 0039).</summary>
public class RebookRulesTests
{
    [Fact]
    public void A_salon_that_set_nothing_is_offered_four_six_and_eight_weeks()
    {
        RebookRules.Choices(new RebookSettings()).ShouldBe([4, 6, 8]);
    }

    [Theory]
    [InlineData(8, 4, 6, new[] { 4, 6, 8 })]
    [InlineData(6, 6, 6, new[] { 6 })]
    [InlineData(0, 5, 0, new[] { 5 })]
    [InlineData(0, 0, 0, new int[0])]
    [InlineData(-1, 53, 52, new[] { 52 })]
    public void Choices_are_each_weeks_once_shortest_first_and_within_a_year(int first, int second, int third, int[] expected)
    {
        RebookRules.Choices(new RebookSettings { FirstWeeks = first, SecondWeeks = second, ThirdWeeks = third }).ShouldBe(expected);
    }

    [Fact]
    public void The_next_visit_is_the_same_weekday_so_many_weeks_on()
    {
        var visit = new LocalDate(2026, 11, 16);

        var day = RebookRules.Day(visit, today: visit, weeks: 6);

        day.ShouldBe(new LocalDate(2026, 12, 28));
        day.DayOfWeek.ShouldBe(visit.DayOfWeek);
    }

    [Fact]
    public void For_a_visit_of_long_ago_the_day_moves_on_by_whole_weeks_until_it_is_ahead()
    {
        var visit = new LocalDate(2026, 6, 1);
        var today = new LocalDate(2026, 11, 16);

        var day = RebookRules.Day(visit, today, weeks: 4);

        day.ShouldBeGreaterThan(today);
        day.DayOfWeek.ShouldBe(visit.DayOfWeek);
        Period.Between(today, day, PeriodUnits.Days).Days.ShouldBeLessThanOrEqualTo(7);
    }
}