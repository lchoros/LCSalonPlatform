using System.Globalization;
using NodaTime;
using Salon.Services.Formatting;

namespace Salon.Tests.Unit.Formatting;

public class DayFormatterTests
{
    [Theory]
    [InlineData(2026, 11, 16, "Monday 16 November 2026")]
    [InlineData(2026, 9, 30, "Wednesday 30 September 2026")]
    [InlineData(2027, 1, 1, "Friday 1 January 2027")]
    public void An_English_day_heading_names_the_weekday_first(int year, int month, int day, string expected)
    {
        new LocalDate(year, month, day).ToDayHeading(CultureInfo.GetCultureInfo("en-GB")).ShouldBe(expected);
    }

    [Fact]
    public void A_Polish_day_heading_names_the_weekday_too()
    {
        new LocalDate(2026, 11, 16).ToDayHeading(CultureInfo.GetCultureInfo("pl-PL")).ShouldStartWith("poniedziałek 16 ");
    }
}