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
public class ShortDateTimeFormatterTests
{
    [Theory]
    [InlineData("en-GB", "16/11/2026 14:05")]
    [InlineData("pl-PL", "16.11.2026 15:05")]
    public void A_moment_is_written_on_the_salons_own_clock_in_the_readers_format(string culture, string expected)
    {
        var at = Instant.FromUtc(2026, 11, 16, 14, 5);
        var zone = DateTimeZoneProviders.Tzdb[culture == "pl-PL" ? "Europe/Warsaw" : "Europe/London"];

        at.ToShortDateTime(zone, CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);
    }

    [Fact]
    public void Summer_time_moves_the_clock_not_the_moment()
    {
        Instant.FromUtc(2026, 7, 1, 9, 30).ToShortDateTime(DateTimeZoneProviders.Tzdb["Europe/London"], CultureInfo.GetCultureInfo("en-GB")).ShouldBe("01/07/2026 10:30");
    }
}