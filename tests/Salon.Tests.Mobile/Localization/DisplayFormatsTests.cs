using System.Globalization;
using Salon.Mobile.Core.Localization;

namespace Salon.Tests.Mobile.Localization;

public class DisplayFormatsTests
{
    private static readonly CultureInfo British = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    [Theory]
    [InlineData(45, "GBP", "en-GB", "£45.00")]
    [InlineData(1234.5, "GBP", "en-GB", "£1,234.50")]
    [InlineData(45, "PLN", "pl-PL", "45,00 zł")]
    [InlineData(45, "PLN", "en-GB", "PLN 45.00")]
    [InlineData(45, "GBP", "pl-PL", "GBP 45,00")]
    [InlineData(-10, "GBP", "en-GB", "−£10.00")]
    [InlineData(-0.004, "GBP", "en-GB", "£0.00")]
    public void Money_reads_the_way_the_web_shows_it(decimal amount, string currency, string culture, string expected)
    {
        DisplayFormats.Money(amount, currency, CultureInfo.GetCultureInfo(culture)).ShouldBe(expected);
    }

    [Fact]
    public void A_price_that_varies_says_from()
    {
        var text = new AppText("en-GB");

        DisplayFormats.Price(text, 16m, "GBP", isFrom: true).ShouldBe("from £16.00");
        DisplayFormats.Price(text, 16m, "GBP", isFrom: false).ShouldBe("£16.00");
        DisplayFormats.Price(new AppText("pl-PL"), 16m, "PLN", isFrom: true).ShouldBe("od 16,00 zł");
    }

    [Fact]
    public void Lengths_are_minutes()
    {
        var text = new AppText();

        DisplayFormats.Minutes(text, 45).ShouldBe("45 min");
        DisplayFormats.MinutesRange(text, 30, 60).ShouldBe("30–60 min");
        DisplayFormats.MinutesRange(text, 30, 30).ShouldBe("30 min");
    }

    [Fact]
    public void Server_dates_are_plain_days_and_anything_else_is_not_one()
    {
        DisplayFormats.ParseDate("2026-11-26").ShouldBe(new DateOnly(2026, 11, 26));
        DisplayFormats.ParseDate("26/11/2026").ShouldBeNull();
        DisplayFormats.ParseDate("2026-11-26T09:00:00Z").ShouldBeNull();
        DisplayFormats.ParseDate(null).ShouldBeNull();
    }

    [Fact]
    public void The_month_title_follows_the_culture()
    {
        DisplayFormats.MonthTitle("2026-11-26", British).ShouldBe("November 2026");
        DisplayFormats.MonthTitle("2026-11-26", Polish).ShouldBe("Listopad 2026");
        DisplayFormats.MonthTitle("nonsense", British).ShouldBe(string.Empty);
    }

    [Fact]
    public void A_long_date_reads_naturally_and_the_text_stands_when_it_is_no_date()
    {
        DisplayFormats.LongDate("2026-11-26", British).ShouldBe("Thursday 26 November 2026");
        DisplayFormats.LongDate("2026-11-26", Polish).ShouldBe("czwartek 26 listopada 2026");
        DisplayFormats.LongDate("tomorrow", British).ShouldBe("tomorrow");
    }
}