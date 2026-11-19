using NodaTime;
using Salon.Core.Reports;

namespace Salon.Tests.Unit.Reports;

/// <summary>ADR 0071: a period longer than two months is drawn by week, Monday to Sunday, cut at its ends.</summary>
public class ChartWeeksTests
{
    [Fact]
    public void Sixty_three_days_from_a_Sunday_are_that_Sunday_eight_whole_weeks_and_six_days()
    {
        var weeks = ReportRules.ChartWeeks(new ReportPeriod(new LocalDate(2026, 3, 1), new LocalDate(2026, 5, 2)));

        weeks.Count.ShouldBe(10);
        weeks[0].ShouldBe(new ReportPeriod(new LocalDate(2026, 3, 1), new LocalDate(2026, 3, 1)));
        weeks[1].ShouldBe(new ReportPeriod(new LocalDate(2026, 3, 2), new LocalDate(2026, 3, 8)));
        weeks[^1].ShouldBe(new ReportPeriod(new LocalDate(2026, 4, 27), new LocalDate(2026, 5, 2)));
    }

    [Theory]
    [InlineData(2026, 1, 1, 62, 0)]
    [InlineData(2026, 1, 1, 63, 10)]
    [InlineData(2026, 1, 1, 90, 14)]
    [InlineData(2026, 1, 1, 365, 53)]
    [InlineData(2028, 1, 1, 366, 53)]
    public void A_period_drawn_day_by_day_has_no_weeks_and_a_year_has_fifty_three_at_most(int year, int month, int day, int length, int count)
    {
        var from = new LocalDate(year, month, day);

        ReportRules.ChartWeeks(new ReportPeriod(from, from.PlusDays(length - 1))).Count.ShouldBe(count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(100)]
    [InlineData(300)]
    public void The_weeks_follow_each_other_without_a_gap_and_cover_the_period(int offset)
    {
        var from = new LocalDate(2026, 1, 1).PlusDays(offset);
        var period = new ReportPeriod(from, from.PlusDays(65 + offset % 200));

        var weeks = ReportRules.ChartWeeks(period);

        weeks[0].From.ShouldBe(period.From);
        weeks[^1].To.ShouldBe(period.To);
        weeks.Zip(weeks.Skip(1)).ShouldAllBe(pair => pair.First.To.PlusDays(1) == pair.Second.From);
        weeks.Skip(1).ShouldAllBe(w => w.From.DayOfWeek == IsoDayOfWeek.Monday);
        weeks.SkipLast(1).ShouldAllBe(w => w.To.DayOfWeek == IsoDayOfWeek.Sunday);
        weeks.ShouldAllBe(w => w.Days >= 1 && w.Days <= 7);
    }
}