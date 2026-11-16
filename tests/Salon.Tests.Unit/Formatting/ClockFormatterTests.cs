using System.Globalization;
using NodaTime;
using Salon.Services.Formatting;

namespace Salon.Tests.Unit.Formatting;

/// <summary>Times, machine-readable dates and placeholders written in one place (ADR 0043).</summary>
public class ClockFormatterTests
{
    [Theory]
    [InlineData("en-GB", "14:05")]
    [InlineData("pl-PL", "14:05")]
    public void A_time_of_day_is_written_as_the_reader_writes_it(string culture, string expected)
    {
        new LocalTime(14, 5).ToShortTime(CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);
    }

    [Fact]
    public void A_rota_time_is_the_same_in_every_language()
    {
        new LocalTime(9, 30).ToClockTime().ShouldBe("09:30");
        new LocalTime(17, 0).ToClockTime().ShouldBe("17:00");
    }

    [Fact]
    public void A_day_and_a_moment_for_an_address_are_iso()
    {
        new LocalDate(2026, 11, 6).ToIso().ShouldBe("2026-11-06");
        Instant.FromUtc(2026, 11, 16, 14, 5, 0).ToIso().ShouldBe("2026-11-16T14:05:00Z");
    }

    [Theory]
    [InlineData(1890, "en-GB", "31.5")]
    [InlineData(1890, "pl-PL", "31,5")]
    [InlineData(2400, "en-GB", "40")]
    [InlineData(0, "en-GB", "0")]
    public void Minutes_read_as_hours_with_at_most_one_decimal(int minutes, string culture, string expected)
    {
        ClockFormatter.MinutesAsHours(minutes, CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("07700 900123", "07700 900123")]
    [InlineData("", "—")]
    [InlineData(null, "—")]
    public void An_empty_value_shows_as_a_dash(string? value, string expected)
    {
        value.OrDash().ShouldBe(expected);
    }
}