using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Import;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0067: dates, times and lengths as an old system's diary export writes them. ISO and day-first only; a file
/// written month-first is recognised so it can be refused instead of moving every visit to another day. A time the
/// clocks skip or pass twice is not guessed.
/// </summary>
public class ImportDatesTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];

    [Theory]
    [InlineData("2026-11-20", 2026, 11, 20)]
    [InlineData("20.11.2026", 2026, 11, 20)]
    [InlineData("20/11/2026", 2026, 11, 20)]
    [InlineData("20-11-2026", 2026, 11, 20)]
    [InlineData(" 3/4/2027 ", 2027, 4, 3)]
    [InlineData("29.02.2028", 2028, 2, 29)]
    public void Iso_and_day_first_dates_are_read(string text, int year, int month, int day)
    {
        ImportDates.TryDate(text, out var date).ShouldBeTrue();
        date.ShouldBe(new LocalDate(year, month, day));
    }

    [Theory]
    [InlineData("20/11/26")]
    [InlineData("11/20/2026")]
    [InlineData("31.04.2027")]
    [InlineData("29.02.2027")]
    [InlineData("2026/11")]
    [InlineData("20 November 2026")]
    [InlineData("46345")]
    [InlineData("2026-13-01")]
    [InlineData("00.11.2026")]
    public void Anything_else_is_not_a_date(string text)
    {
        ImportDates.TryDate(text, out var date).ShouldBeFalse(text);
        date.ShouldBeNull();
    }

    [Fact]
    public void A_blank_cell_is_no_date_and_no_problem()
    {
        ImportDates.TryDate("  ", out var date).ShouldBeTrue();
        date.ShouldBeNull();
        ImportDates.TryTime(null, out var time).ShouldBeTrue();
        time.ShouldBeNull();
        ImportDates.TryMinutes(string.Empty, out var minutes).ShouldBeTrue();
        minutes.ShouldBeNull();
    }

    [Theory]
    [InlineData("11/20/2026", true)]
    [InlineData("12/13/2026", true)]
    [InlineData("20/11/2026", false)]
    [InlineData("03/04/2027", false)]
    [InlineData("2026-11-20", false)]
    [InlineData("2026-11-20 14:30", false)]
    [InlineData("", false)]
    public void Only_a_date_that_cannot_be_day_first_looks_month_first(string text, bool monthFirst)
    {
        ImportDates.LooksMonthFirst(ImportDates.DatePart(text)).ShouldBe(monthFirst, text);
    }

    [Theory]
    [InlineData("14:30", 14, 30)]
    [InlineData("9:05", 9, 5)]
    [InlineData("09:05:00", 9, 5)]
    [InlineData("14.30", 14, 30)]
    [InlineData("2:30 PM", 14, 30)]
    [InlineData("2.30pm", 14, 30)]
    [InlineData("12:00 AM", 0, 0)]
    [InlineData("12:15 pm", 12, 15)]
    [InlineData("0:00", 0, 0)]
    public void Times_are_read_on_the_24_hour_and_the_12_hour_clock(string text, int hour, int minute)
    {
        ImportDates.TryTime(text, out var time).ShouldBeTrue(text);
        time.ShouldBe(new LocalTime(hour, minute));
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("14:60")]
    [InlineData("14:3")]
    [InlineData("14")]
    [InlineData("13:00 PM")]
    [InlineData("0:30 AM")]
    [InlineData("14:30:15")]
    [InlineData("half past two")]
    public void Anything_else_is_not_a_time(string text)
    {
        ImportDates.TryTime(text, out var time).ShouldBeFalse(text);
        time.ShouldBeNull();
    }

    [Theory]
    [InlineData("2026-11-20 14:30")]
    [InlineData("2026-11-20T14:30:00")]
    [InlineData("20.11.2026 14:30")]
    [InlineData("20/11/2026 2:30 PM")]
    public void A_date_and_a_time_in_one_cell_are_read(string text)
    {
        ImportDates.TryDateTime(text, out var moment).ShouldBeTrue(text);
        moment.ShouldBe(new LocalDateTime(2026, 11, 20, 14, 30));
    }

    [Theory]
    [InlineData("2026-11-20")]
    [InlineData("14:30")]
    [InlineData("2026-11-20 later")]
    public void A_date_without_a_time_is_not_a_moment(string text)
    {
        ImportDates.TryDateTime(text, out var moment).ShouldBeFalse(text);
        moment.ShouldBeNull();
    }

    [Theory]
    [InlineData("45", 45)]
    [InlineData("45 min", 45)]
    [InlineData("1:30", 90)]
    [InlineData("1h 30", 90)]
    [InlineData("1h30m", 90)]
    [InlineData("2h", 120)]
    [InlineData("0:45", 45)]
    public void A_length_is_minutes_or_hours_and_minutes(string text, int minutes)
    {
        ImportDates.TryMinutes(text, out var read).ShouldBeTrue(text);
        read.ShouldBe(minutes);
    }

    [Theory]
    [InlineData("1:3")]
    [InlineData("1:75")]
    [InlineData("45.5")]
    [InlineData("an hour")]
    public void Anything_else_is_not_a_length(string text)
    {
        ImportDates.TryMinutes(text, out var read).ShouldBeFalse(text);
        read.ShouldBeNull();
    }

    [Fact]
    public void A_time_the_clocks_skip_or_pass_twice_is_not_guessed()
    {
        // In 2027 the clocks go forward at 01:00 on 28 March and back at 02:00 on 31 October.
        var skipped = new LocalDateTime(2027, 3, 28, 1, 30);
        var twice = new LocalDateTime(2027, 10, 31, 1, 30);
        var ordinary = new LocalDateTime(2027, 3, 28, 10, 0);

        ImportDates.InZone(skipped, London).ShouldBeNull();
        ImportDates.Mappings(skipped, London).ShouldBe(0);
        ImportDates.InZone(twice, London).ShouldBeNull();
        ImportDates.Mappings(twice, London).ShouldBe(2);
        ImportDates.InZone(ordinary, London).ShouldBe(Instant.FromUtc(2027, 3, 28, 9, 0), "British Summer Time is an hour ahead");
    }

    [Property(MaxTest = 300)]
    public Property Every_day_written_in_every_accepted_way_reads_back_as_that_day()
    {
        var day = from offset in Gen.Choose(0, 3 * 366)
                  select new LocalDate(2026, 1, 1).PlusDays(offset);
        var patterns = new[] { "uuuu'-'MM'-'dd", "dd'.'MM'.'uuuu", "dd'/'MM'/'uuuu", "d'/'M'/'uuuu", "dd'-'MM'-'uuuu" };

        return Prop.ForAll(day.ToArbitrary(), d => patterns.All(p =>
            ImportDates.TryDate(LocalDatePattern.CreateWithInvariantCulture(p).Format(d), out var read) && read == d
            && !ImportDates.LooksMonthFirst(LocalDatePattern.CreateWithInvariantCulture(p).Format(d))));
    }
}