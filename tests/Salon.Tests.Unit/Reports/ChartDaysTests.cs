using NodaTime;
using Salon.Core.Reports;

namespace Salon.Tests.Unit.Reports;

/// <summary>ADR 0048: which periods are drawn day by day, and which days the bars are of.</summary>
public class ChartDaysTests
{
    private static readonly LocalDate First = new(2026, 3, 1);

    [Fact]
    public void A_week_is_drawn_as_its_seven_days_in_order()
    {
        var days = ReportRules.ChartDays(new ReportPeriod(new LocalDate(2026, 3, 2), new LocalDate(2026, 3, 8)));

        days.Count.ShouldBe(7);
        days[0].ShouldBe(new LocalDate(2026, 3, 2));
        days[^1].ShouldBe(new LocalDate(2026, 3, 8));
        days.ShouldBe(Enumerable.Range(0, 7).Select(new LocalDate(2026, 3, 2).PlusDays), "every day, in order");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(31, 31)]
    [InlineData(62, 62)]
    [InlineData(63, 0)]
    [InlineData(366, 0)]
    public void One_day_is_one_number_and_more_than_two_months_are_too_many_bars(int length, int bars)
    {
        ReportRules.ChartDays(new ReportPeriod(First, First.PlusDays(length - 1))).Count.ShouldBe(bars);
    }
}