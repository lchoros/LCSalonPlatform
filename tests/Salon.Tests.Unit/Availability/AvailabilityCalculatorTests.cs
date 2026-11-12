using NodaTime;

using Salon.Core.Staff;
using Salon.Services.Availability;

namespace Salon.Tests.Unit.Availability;

/// <summary>Worked examples for the pure engine (ADR 0012): steps, buffers, notice, advance limit, split shifts and both DST days.</summary>
public class AvailabilityCalculatorTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Guid Anna = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000001");
    private static readonly Guid Tom = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000002");

    /// <summary>A Monday in June 2026 (BST); "now" is the Sunday before at noon UTC, so notice and advance rules do not interfere.</summary>
    private static readonly LocalDate Monday = new(2026, 6, 15);
    private static readonly Instant SundayNoon = Instant.FromUtc(2026, 6, 14, 12, 0);

    [Fact]
    public void A_nine_to_five_day_with_30_minute_service_and_15_minute_step_has_a_slot_every_quarter_hour()
    {
        var slots = AvailabilityCalculator.Slots(Request(Monday, Monday, Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "17:00"))));

        slots.Count.ShouldBe(31); // 09:00 … 16:30 inclusive
        LocalTimes(slots).First().ShouldBe(new LocalTime(9, 0));
        LocalTimes(slots).Last().ShouldBe(new LocalTime(16, 30));
        LocalTimes(slots).ShouldAllBe(t => t.Minute % 15 == 0);
        slots.ShouldAllBe(s => s.End - s.Start == Duration.FromMinutes(30));
        slots.ShouldAllBe(s => s.StaffMemberId == Anna);
        slots.Select(s => s.Start).ShouldBeInOrder();
    }

    [Fact]
    public void A_range_that_starts_off_the_grid_begins_at_the_next_step()
    {
        var slots = AvailabilityCalculator.Slots(Request(Monday, Monday, Staff(Anna, Day(IsoDayOfWeek.Monday, "09:05", "10:00"))));

        LocalTimes(slots).ShouldBe([new LocalTime(9, 15), new LocalTime(9, 30)]);
    }

    [Fact]
    public void A_split_shift_yields_slots_in_both_halves_and_none_across_the_gap()
    {
        var staff = Staff(Anna, Day(IsoDayOfWeek.Monday, ("09:00", "13:00"), ("14:00", "17:00")));

        var times = LocalTimes(AvailabilityCalculator.Slots(Request(Monday, Monday, staff, serviceMinutes: 60)));

        times.ShouldContain(new LocalTime(12, 0));
        times.ShouldNotContain(new LocalTime(12, 15)); // would end 13:15, outside the morning range
        times.ShouldNotContain(new LocalTime(13, 0));
        times.ShouldNotContain(new LocalTime(13, 45));
        times.ShouldContain(new LocalTime(14, 0));
        times.Last().ShouldBe(new LocalTime(16, 0));
    }

    [Fact]
    public void A_busy_interval_removes_every_slot_that_overlaps_it_including_the_buffer()
    {
        var busy = new Interval(At(Monday, 10, 0), At(Monday, 10, 30));
        var staff = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "12:00"), busy);

        var times = LocalTimes(AvailabilityCalculator.Slots(Request(Monday, Monday, staff, serviceMinutes: 30, bufferMinutes: 10)));

        // 09:15 + 30 min + 10 min buffer = 09:55 fits; 09:30 + 30 + 10 = 10:10 overlaps; 10:30 starts when the busy range ends.
        times.ShouldContain(new LocalTime(9, 15));
        times.ShouldNotContain(new LocalTime(9, 30));
        times.ShouldNotContain(new LocalTime(9, 45));
        times.ShouldNotContain(new LocalTime(10, 0));
        times.ShouldNotContain(new LocalTime(10, 15));
        times.ShouldContain(new LocalTime(10, 30));
    }

    [Fact]
    public void The_buffer_must_fit_inside_the_working_range_but_is_not_part_of_the_slot()
    {
        var staff = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "10:00"));

        var slots = AvailabilityCalculator.Slots(Request(Monday, Monday, staff, serviceMinutes: 30, bufferMinutes: 15));

        LocalTimes(slots).ShouldBe([new LocalTime(9, 0), new LocalTime(9, 15)]); // 09:30 + 30 + 15 = 10:15 does not fit
        slots.ShouldAllBe(s => s.End - s.Start == Duration.FromMinutes(30));
    }

    [Fact]
    public void Slots_earlier_than_now_plus_the_minimum_notice_are_left_out()
    {
        var now = At(Monday, 9, 50);
        var staff = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "12:00"));

        var times = LocalTimes(AvailabilityCalculator.Slots(Request(Monday, Monday, staff, now: now, minNoticeMinutes: 60)));

        times.First().ShouldBe(new LocalTime(11, 0)); // 10:45 < 09:50 + 60 min
    }

    [Fact]
    public void Days_after_today_plus_the_advance_limit_have_no_slots()
    {
        var staff = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "12:00"), Day(IsoDayOfWeek.Tuesday, "09:00", "12:00"));
        var request = Request(Monday, Monday.PlusDays(1), staff, maxAdvanceDays: 1); // now is Sunday 14 June: last bookable day is Monday

        var slots = AvailabilityCalculator.Slots(request);

        slots.ShouldNotBeEmpty();
        slots.ShouldAllBe(s => s.Start.InZone(London).Date == Monday);
    }

    [Fact]
    public void Days_off_and_days_outside_the_range_produce_nothing()
    {
        var staff = Staff(Anna, Day(IsoDayOfWeek.Tuesday, "09:00", "12:00"));

        AvailabilityCalculator.Slots(Request(Monday, Monday, staff)).ShouldBeEmpty();
        AvailabilityCalculator.Slots(Request(Monday, Monday.PlusDays(1), staff)).ShouldAllBe(s => s.Start.InZone(London).Date == Monday.PlusDays(1));
    }

    [Theory]
    [InlineData(2026, 3, 29)] // clocks go forward: 01:00 GMT → 02:00 BST
    [InlineData(2026, 10, 25)] // clocks go back: 02:00 BST → 01:00 GMT
    public void A_dst_change_day_produces_the_same_step_aligned_slots_as_any_other_day(int year, int month, int day)
    {
        var dstDay = new LocalDate(year, month, day);
        var now = London.AtStartOfDay(dstDay.PlusDays(-7)).ToInstant();
        var staff = Staff(Anna, Day(dstDay.DayOfWeek, "09:00", "17:00"));

        var slots = AvailabilityCalculator.Slots(Request(dstDay, dstDay, staff, now: now));

        slots.Count.ShouldBe(31);
        LocalTimes(slots, London).First().ShouldBe(new LocalTime(9, 0));
        LocalTimes(slots, London).Last().ShouldBe(new LocalTime(16, 30));
        LocalTimes(slots, London).ShouldAllBe(t => t.Minute % 15 == 0);
        slots.ShouldAllBe(s => s.Start.InZone(London).Date == dstDay);
        slots.Select(s => s.Start).ShouldBeUnique();
    }

    [Fact]
    public void A_shift_across_the_spring_forward_gap_never_throws_and_reports_each_instant_once()
    {
        var dstDay = new LocalDate(2026, 3, 29);
        var now = London.AtStartOfDay(dstDay.PlusDays(-7)).ToInstant();
        var staff = Staff(Anna, Day(IsoDayOfWeek.Sunday, "00:00", "04:00"));

        var slots = Should.NotThrow(() => AvailabilityCalculator.Slots(Request(dstDay, dstDay, staff, now: now, serviceMinutes: 30)));

        slots.ShouldNotBeEmpty();
        slots.Select(s => s.Start).ShouldBeUnique();
        slots.Select(s => s.Start).ShouldBeInOrder();
        // 01:00–01:59 local does not exist; AtLeniently moves those starts forward onto the 02:00 BST instants, which are reported once.
        slots.Count(s => s.Start == Instant.FromUtc(2026, 3, 29, 1, 0)).ShouldBe(1);
    }

    [Fact]
    public void A_shift_across_the_fall_back_overlap_never_throws_and_keeps_the_first_occurrence()
    {
        var dstDay = new LocalDate(2026, 10, 25);
        var now = London.AtStartOfDay(dstDay.PlusDays(-7)).ToInstant();
        var staff = Staff(Anna, Day(IsoDayOfWeek.Sunday, "00:00", "04:00"));

        var slots = Should.NotThrow(() => AvailabilityCalculator.Slots(Request(dstDay, dstDay, staff, now: now, serviceMinutes: 30)));

        slots.Select(s => s.Start).ShouldBeUnique();
        // 01:30 happens twice that night; the earlier (BST) one is the slot, i.e. 00:30 UTC.
        slots.ShouldContain(s => s.Start == Instant.FromUtc(2026, 10, 25, 0, 30));
        slots.ShouldNotContain(s => s.Start == Instant.FromUtc(2026, 10, 25, 1, 30));
    }

    [Fact]
    public void Slots_of_several_staff_are_ordered_by_start_then_by_staff_order()
    {
        var anna = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "10:00"));
        var tom = Staff(Tom, Day(IsoDayOfWeek.Monday, "09:00", "10:00"));

        var slots = AvailabilityCalculator.Slots(Request(Monday, Monday, [anna, tom]));

        slots.Select(s => (Time: s.Start.InZone(London).TimeOfDay, s.StaffMemberId)).ShouldBe(
        [
            (new LocalTime(9, 0), Anna), (new LocalTime(9, 0), Tom),
            (new LocalTime(9, 15), Anna), (new LocalTime(9, 15), Tom),
            (new LocalTime(9, 30), Anna), (new LocalTime(9, 30), Tom),
        ]);
    }

    [Fact]
    public void Any_staff_keeps_one_slot_per_start_and_prefers_the_person_with_fewer_bookings_that_day()
    {
        var annaBusy = new Interval(At(Monday, 14, 0), At(Monday, 15, 0));
        var anna = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "10:00"), annaBusy);
        var tom = Staff(Tom, Day(IsoDayOfWeek.Monday, "09:15", "10:30"));

        var slots = AvailabilityCalculator.ForAnyStaff(Request(Monday, Monday, [anna, tom]));

        slots.Select(s => s.Start).ShouldBeUnique();
        slots.Select(s => (Time: s.Start.InZone(London).TimeOfDay, s.StaffMemberId)).ShouldBe(
        [
            (new LocalTime(9, 0), Anna), // only Anna works at 09:00
            (new LocalTime(9, 15), Tom), // both free; Tom has no bookings that day, Anna has one
            (new LocalTime(9, 30), Tom),
            (new LocalTime(9, 45), Tom),
            (new LocalTime(10, 0), Tom),
        ]);
    }

    [Fact]
    public void Any_staff_ties_go_to_the_staff_member_listed_first()
    {
        var anna = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "10:00"));
        var tom = Staff(Tom, Day(IsoDayOfWeek.Monday, "09:00", "10:00"));

        AvailabilityCalculator.ForAnyStaff(Request(Monday, Monday, [anna, tom])).ShouldAllBe(s => s.StaffMemberId == Anna);
        AvailabilityCalculator.ForAnyStaff(Request(Monday, Monday, [tom, anna])).ShouldAllBe(s => s.StaffMemberId == Tom);
    }

    [Fact]
    public void Invalid_inputs_are_rejected()
    {
        var staff = Staff(Anna, Day(IsoDayOfWeek.Monday, "09:00", "10:00"));

        Should.Throw<ArgumentOutOfRangeException>(() => AvailabilityCalculator.Slots(Request(Monday, Monday, staff) with { StepMinutes = 0 }));
        Should.Throw<ArgumentOutOfRangeException>(() => AvailabilityCalculator.Slots(Request(Monday, Monday, staff) with { ServiceDuration = Duration.Zero }));
        Should.Throw<ArgumentOutOfRangeException>(() => AvailabilityCalculator.Slots(Request(Monday, Monday, staff) with { Buffer = Duration.FromMinutes(-1) }));
        Should.Throw<ArgumentException>(() => AvailabilityCalculator.Slots(Request(Monday, Monday.PlusDays(-1), staff)));
        Should.Throw<ArgumentNullException>(() => AvailabilityCalculator.Slots(null!));
    }

    internal static AvailabilityRequest Request(
        LocalDate from,
        LocalDate to,
        StaffAvailabilityInput staff,
        int serviceMinutes = 30,
        int bufferMinutes = 0,
        int stepMinutes = 15,
        Instant? now = null,
        int minNoticeMinutes = 0,
        int maxAdvanceDays = 365)
        => Request(from, to, [staff], serviceMinutes, bufferMinutes, stepMinutes, now, minNoticeMinutes, maxAdvanceDays);

    internal static AvailabilityRequest Request(
        LocalDate from,
        LocalDate to,
        IReadOnlyList<StaffAvailabilityInput> staff,
        int serviceMinutes = 30,
        int bufferMinutes = 0,
        int stepMinutes = 15,
        Instant? now = null,
        int minNoticeMinutes = 0,
        int maxAdvanceDays = 365)
        => new(
            London,
            from,
            to,
            Duration.FromMinutes(serviceMinutes),
            Duration.FromMinutes(bufferMinutes),
            stepMinutes,
            now ?? SundayNoon,
            Duration.FromMinutes(minNoticeMinutes),
            maxAdvanceDays,
            staff);

    internal static StaffAvailabilityInput Staff(Guid id, params KeyValuePair<IsoDayOfWeek, IReadOnlyList<TimeRange>>[] days)
        => Staff(id, days, []);

    internal static StaffAvailabilityInput Staff(Guid id, KeyValuePair<IsoDayOfWeek, IReadOnlyList<TimeRange>> day, params Interval[] busy)
        => Staff(id, [day], busy);

    internal static StaffAvailabilityInput Staff(Guid id, IEnumerable<KeyValuePair<IsoDayOfWeek, IReadOnlyList<TimeRange>>> days, IReadOnlyList<Interval> busy)
        => new(id, days.ToDictionary(d => d.Key, d => d.Value), busy);

    internal static KeyValuePair<IsoDayOfWeek, IReadOnlyList<TimeRange>> Day(IsoDayOfWeek day, string start, string end)
        => Day(day, (start, end));

    internal static KeyValuePair<IsoDayOfWeek, IReadOnlyList<TimeRange>> Day(IsoDayOfWeek day, params (string Start, string End)[] ranges)
        => KeyValuePair.Create(day, (IReadOnlyList<TimeRange>)ranges.Select(r => new TimeRange(Time(r.Start), Time(r.End))).ToArray());

    internal static LocalTime Time(string text) => WorkingHoursRules.TryParseTime(text) ?? throw new FormatException(text);

    internal static Instant At(LocalDate day, int hour, int minute) => London.AtLeniently(day.At(new LocalTime(hour, minute))).ToInstant();

    internal static List<LocalTime> LocalTimes(IReadOnlyList<Slot> slots, DateTimeZone? zone = null)
        => slots.Select(s => s.Start.InZone(zone ?? London).TimeOfDay).ToList();
}
