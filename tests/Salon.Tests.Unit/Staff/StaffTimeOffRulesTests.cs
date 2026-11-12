using NodaTime;

using Salon.Core.Staff;

namespace Salon.Tests.Unit.Staff;

/// <summary>
/// The pure time-off rules of ADR 0015: date parsing, the validation table, overlap detection between two entries of one
/// person, and the busy intervals an entry becomes in the salon's zone, including both 2026 DST days in London and Warsaw.
/// </summary>
public class StaffTimeOffRulesTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly DateTimeZone Warsaw = DateTimeZoneProviders.Tzdb["Europe/Warsaw"];
    private static readonly Guid Anna = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000001");
    private static readonly Guid Tom = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000002");

    [Theory]
    [InlineData("2026-03-29", 2026, 3, 29)]
    [InlineData(" 2026-12-01 ", 2026, 12, 1)]
    public void Parses_what_a_date_input_posts(string text, int year, int month, int day)
    {
        StaffTimeOffRules.TryParseDate(text).ShouldBe(new LocalDate(year, month, day));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("29/03/2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-3-9")]
    [InlineData("tomorrow")]
    public void Anything_else_does_not_parse(string? text)
    {
        StaffTimeOffRules.TryParseDate(text).ShouldBeNull();
    }

    [Fact]
    public void Day_count_includes_both_ends()
    {
        StaffTimeOffRules.DayCount(new LocalDate(2026, 6, 1), new LocalDate(2026, 6, 1)).ShouldBe(1);
        StaffTimeOffRules.DayCount(new LocalDate(2026, 6, 1), new LocalDate(2026, 6, 7)).ShouldBe(7);
        StaffTimeOffRules.DayCount(new LocalDate(2026, 6, 2), new LocalDate(2026, 6, 1)).ShouldBe(0);
    }

    [Theory]
    [InlineData("2026-06-01", "2026-06-01", null, null, null, new StaffTimeOffProblem[0])]
    [InlineData("2026-06-01", "2026-06-14", "09:00", "12:00", "Course", new StaffTimeOffProblem[0])]
    [InlineData("2026-01-01", "2027-01-01", null, null, null, new StaffTimeOffProblem[0])] // 366 days: the limit itself
    [InlineData("2026-01-01", "2027-01-02", null, null, null, new[] { StaffTimeOffProblem.SpanTooLong })]
    [InlineData("2026-06-02", "2026-06-01", null, null, null, new[] { StaffTimeOffProblem.ToBeforeFrom })]
    [InlineData("2026-06-01", "2026-06-01", "09:00", null, null, new[] { StaffTimeOffProblem.TimesIncomplete })]
    [InlineData("2026-06-01", "2026-06-01", null, "12:00", null, new[] { StaffTimeOffProblem.TimesIncomplete })]
    [InlineData("2026-06-01", "2026-06-01", "12:00", "09:00", null, new[] { StaffTimeOffProblem.EndNotAfterStart })]
    [InlineData("2026-06-01", "2026-06-01", "09:00", "09:00", null, new[] { StaffTimeOffProblem.EndNotAfterStart })]
    [InlineData("2026-06-01", "2026-06-01", "09:03", "12:00", null, new[] { StaffTimeOffProblem.OffStep })]
    [InlineData("2026-06-01", "2026-06-01", "09:00", "12:01", null, new[] { StaffTimeOffProblem.OffStep })]
    [InlineData("2026-06-01", "2026-06-01", "12:01", "09:00", null, new[] { StaffTimeOffProblem.OffStep, StaffTimeOffProblem.EndNotAfterStart })]
    [InlineData("2026-06-02", "2026-06-01", "09:00", null, null, new[] { StaffTimeOffProblem.ToBeforeFrom, StaffTimeOffProblem.TimesIncomplete })]
    public void The_validation_table(string from, string to, string? start, string? end, string? reason, StaffTimeOffProblem[] expected)
    {
        var problems = StaffTimeOffRules.Problems(Date(from), Date(to), Time(start), Time(end), reason);

        problems.ShouldBe(expected);
    }

    [Fact]
    public void A_reason_may_have_200_characters_after_trimming_and_blank_means_none()
    {
        StaffTimeOffRules.ReasonFits(null).ShouldBeTrue();
        StaffTimeOffRules.ReasonFits("   ").ShouldBeTrue();
        StaffTimeOffRules.ReasonFits(new string('x', 200)).ShouldBeTrue();
        StaffTimeOffRules.ReasonFits(" " + new string('x', 200) + " ").ShouldBeTrue();
        StaffTimeOffRules.ReasonFits(new string('x', 201)).ShouldBeFalse();
        StaffTimeOffRules.Problems(Date("2026-06-01"), Date("2026-06-01"), null, null, new string('x', 201)).ShouldBe([StaffTimeOffProblem.ReasonTooLong]);
        StaffTimeOffRules.NormalizeReason("  Holiday ").ShouldBe("Holiday");
        StaffTimeOffRules.NormalizeReason("  ").ShouldBeNull();
    }

    [Fact]
    public void Whole_day_entries_overlap_when_they_share_a_date_and_not_when_they_are_adjacent()
    {
        var holiday = Entry(Anna, "2026-07-06", "2026-07-10");

        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-10", "2026-07-12")).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-01", "2026-07-06")).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-08", "2026-07-08")).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-11", "2026-07-12")).ShouldBeFalse();
        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-01", "2026-07-05")).ShouldBeFalse();
    }

    [Fact]
    public void A_whole_day_entry_overlaps_any_partial_entry_on_a_shared_date()
    {
        var holiday = Entry(Anna, "2026-07-06", "2026-07-10");

        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-10", "2026-07-10", "14:00", "15:00")).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(Entry(Anna, "2026-07-10", "2026-07-10", "14:00", "15:00"), holiday).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(holiday, Entry(Anna, "2026-07-11", "2026-07-11", "14:00", "15:00")).ShouldBeFalse();
    }

    [Fact]
    public void Partial_entries_on_shared_dates_overlap_only_when_their_daily_ranges_do()
    {
        var mornings = Entry(Anna, "2026-07-06", "2026-07-10", "09:00", "12:00");

        StaffTimeOffRules.Overlaps(mornings, Entry(Anna, "2026-07-08", "2026-07-08", "11:55", "13:00")).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(mornings, Entry(Anna, "2026-07-08", "2026-07-08", "10:00", "11:00")).ShouldBeTrue();
        StaffTimeOffRules.Overlaps(mornings, Entry(Anna, "2026-07-08", "2026-07-08", "12:00", "13:00")).ShouldBeFalse(); // touching
        StaffTimeOffRules.Overlaps(mornings, Entry(Anna, "2026-07-01", "2026-07-20", "14:00", "15:00")).ShouldBeFalse(); // afternoons
    }

    [Fact]
    public void Entries_of_different_people_never_overlap()
    {
        StaffTimeOffRules.Overlaps(Entry(Anna, "2026-07-06", "2026-07-10"), Entry(Tom, "2026-07-06", "2026-07-10")).ShouldBeFalse();
    }

    [Fact]
    public void A_whole_day_span_becomes_one_interval_per_day_covering_the_local_day()
    {
        var intervals = StaffTimeOffRules.ToIntervals(Entry(Anna, "2026-06-15", "2026-06-17"), London);

        intervals.Count.ShouldBe(3);
        intervals[0].ShouldBe(new Interval(Instant.FromUtc(2026, 6, 14, 23, 0), Instant.FromUtc(2026, 6, 15, 23, 0))); // BST = UTC+1
        intervals[1].Start.ShouldBe(intervals[0].End);
        intervals[2].End.ShouldBe(Instant.FromUtc(2026, 6, 17, 23, 0));
        intervals.ShouldAllBe(i => i.Duration == Duration.FromHours(24));
    }

    [Fact]
    public void A_partial_span_becomes_the_same_local_range_on_each_day()
    {
        var intervals = StaffTimeOffRules.ToIntervals(Entry(Anna, "2026-06-15", "2026-06-16", "14:00", "15:30"), Warsaw);

        intervals.ShouldBe(
        [
            new Interval(Instant.FromUtc(2026, 6, 15, 12, 0), Instant.FromUtc(2026, 6, 15, 13, 30)), // CEST = UTC+2
            new Interval(Instant.FromUtc(2026, 6, 16, 12, 0), Instant.FromUtc(2026, 6, 16, 13, 30)),
        ]);
    }

    [Theory]
    [InlineData("Europe/London", 2026, 3, 29, 23)] // clocks go forward: a 23-hour day
    [InlineData("Europe/London", 2026, 10, 25, 25)] // clocks go back: a 25-hour day
    [InlineData("Europe/Warsaw", 2026, 3, 29, 23)]
    [InlineData("Europe/Warsaw", 2026, 10, 25, 25)]
    public void A_whole_day_on_a_dst_change_day_covers_the_real_length_of_that_day(string zoneId, int year, int month, int day, int hours)
    {
        var zone = DateTimeZoneProviders.Tzdb[zoneId];
        var date = new LocalDate(year, month, day);

        var interval = StaffTimeOffRules.ToIntervals(Entry(Anna, date, date), zone).ShouldHaveSingleItem();

        interval.Start.ShouldBe(zone.AtStartOfDay(date).ToInstant());
        interval.End.ShouldBe(zone.AtStartOfDay(date.PlusDays(1)).ToInstant());
        interval.Duration.ShouldBe(Duration.FromHours(hours));
    }

    [Fact]
    public void A_partial_range_in_the_spring_forward_gap_is_moved_forward_leniently_and_never_throws()
    {
        var day = new LocalDate(2026, 3, 29);

        // 01:00 local does not exist in London that night; leniently it becomes 02:00 BST = 01:00 UTC, so 01:00–03:00 lasts one hour.
        var london = Should.NotThrow(() => StaffTimeOffRules.ToIntervals(Entry(Anna, day, day, "01:00", "03:00"), London)).ShouldHaveSingleItem();
        london.ShouldBe(new Interval(Instant.FromUtc(2026, 3, 29, 1, 0), Instant.FromUtc(2026, 3, 29, 2, 0)));

        // 02:00–03:00 does not exist in Warsaw that night: both ends collapse onto 03:00 CEST, so there is nothing to block.
        StaffTimeOffRules.ToIntervals(Entry(Anna, day, day, "02:00", "03:00"), Warsaw).ShouldBeEmpty();
        // 02:30–04:00 in Warsaw: the start is shifted forward by the hour that was skipped, to 03:30 CEST = 01:30 UTC; the end is 04:00 CEST = 02:00 UTC.
        StaffTimeOffRules.ToIntervals(Entry(Anna, day, day, "02:30", "04:00"), Warsaw).ShouldHaveSingleItem()
            .ShouldBe(new Interval(Instant.FromUtc(2026, 3, 29, 1, 30), Instant.FromUtc(2026, 3, 29, 2, 0)));
    }

    [Fact]
    public void A_partial_range_across_the_fall_back_overlap_takes_the_first_occurrence_of_the_start()
    {
        var day = new LocalDate(2026, 10, 25);

        // London: 01:00 happens twice; leniently the earlier (BST) one is taken = 00:00 UTC; 02:00 GMT = 02:00 UTC.
        StaffTimeOffRules.ToIntervals(Entry(Anna, day, day, "01:00", "02:00"), London).ShouldHaveSingleItem()
            .ShouldBe(new Interval(Instant.FromUtc(2026, 10, 25, 0, 0), Instant.FromUtc(2026, 10, 25, 2, 0)));

        // Warsaw: 02:00 happens twice; the earlier (CEST) one = 00:00 UTC; 03:00 CET = 02:00 UTC.
        StaffTimeOffRules.ToIntervals(Entry(Anna, day, day, "02:00", "03:00"), Warsaw).ShouldHaveSingleItem()
            .ShouldBe(new Interval(Instant.FromUtc(2026, 10, 25, 0, 0), Instant.FromUtc(2026, 10, 25, 2, 0)));

        // An ordinary afternoon on that day is unaffected.
        StaffTimeOffRules.ToIntervals(Entry(Anna, day, day, "14:00", "15:00"), London).ShouldHaveSingleItem()
            .ShouldBe(new Interval(Instant.FromUtc(2026, 10, 25, 14, 0), Instant.FromUtc(2026, 10, 25, 15, 0)));
    }

    [Fact]
    public void Intervals_of_a_span_across_a_dst_change_stay_ordered_and_one_per_day()
    {
        var intervals = StaffTimeOffRules.ToIntervals(Entry(Anna, "2026-03-28", "2026-03-30", "09:00", "17:00"), London);

        intervals.Count.ShouldBe(3);
        intervals.Select(i => i.Start).ShouldBeInOrder();
        intervals.ShouldAllBe(i => i.Duration == Duration.FromHours(8));
        intervals[0].Start.ShouldBe(Instant.FromUtc(2026, 3, 28, 9, 0)); // GMT
        intervals[1].Start.ShouldBe(Instant.FromUtc(2026, 3, 29, 8, 0)); // BST
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Should.Throw<ArgumentNullException>(() => StaffTimeOffRules.ToIntervals(null!, London));
        Should.Throw<ArgumentNullException>(() => StaffTimeOffRules.ToIntervals(Entry(Anna, "2026-06-01", "2026-06-01"), null!));
        Should.Throw<ArgumentNullException>(() => StaffTimeOffRules.Overlaps(null!, Entry(Anna, "2026-06-01", "2026-06-01")));
    }

    internal static StaffTimeOff Entry(Guid staffId, string from, string to, string? start = null, string? end = null)
        => Entry(staffId, Date(from), Date(to), start, end);

    internal static StaffTimeOff Entry(Guid staffId, LocalDate from, LocalDate to, string? start = null, string? end = null)
        => new() { StaffMemberId = staffId, FromDate = from, ToDate = to, Start = Time(start), End = Time(end) };

    private static LocalDate Date(string text) => StaffTimeOffRules.TryParseDate(text) ?? throw new FormatException(text);

    private static LocalTime? Time(string? text) => text is null ? null : WorkingHoursRules.TryParseTime(text) ?? throw new FormatException(text);
}