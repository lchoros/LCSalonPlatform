using NodaTime;
using Salon.Core.Staff;
using Salon.Services.Availability;
using static Salon.Tests.Unit.Availability.AvailabilityCalculatorTests;

namespace Salon.Tests.Unit.Availability;

/// <summary>
/// Time off (ADR 0015) reaches the pure engine as busy intervals produced by <see cref="StaffTimeOffRules.ToIntervals"/>:
/// a whole day off removes every slot of that day and nothing else; a partial day removes only the slots that would touch
/// the blocked range.
/// </summary>
public class AvailabilityCalculatorTimeOffTests
{
    private static readonly DateTimeZone London = DateTimeZoneProviders.Tzdb["Europe/London"];
    private static readonly Guid Anna = Guid.Parse("0190a1b2-c3d4-7e5f-8a9b-000000000001");
    private static readonly LocalDate Monday = new(2026, 6, 15);
    private static readonly LocalDate Tuesday = Monday.PlusDays(1);

    [Fact]
    public void A_whole_day_off_removes_every_slot_of_that_day_and_leaves_the_next_day_untouched()
    {
        var dayOff = StaffTimeOffRules.ToIntervals(Entry(Monday, Monday), London);
        var staff = AvailabilityCalculatorTests.Staff(Anna, Rota(), dayOff);

        var slots = AvailabilityCalculator.Slots(Request(Monday, Tuesday, staff));

        slots.ShouldNotBeEmpty();
        slots.ShouldAllBe(s => s.Start.InZone(London).Date == Tuesday);
        slots.Count.ShouldBe(31); // Tuesday 09:00 … 16:30, exactly as a normal day
        AvailabilityCalculator.Slots(Request(Monday, Tuesday, AvailabilityCalculatorTests.Staff(Anna, Rota(), []))).Count.ShouldBe(62);
    }

    [Fact]
    public void A_multi_day_whole_day_span_removes_every_day_it_covers()
    {
        var holiday = StaffTimeOffRules.ToIntervals(Entry(Monday, Monday.PlusDays(4)), London);
        var staff = AvailabilityCalculatorTests.Staff(Anna, Rota(), holiday);

        AvailabilityCalculator.Slots(Request(Monday, Monday.PlusDays(4), staff)).ShouldBeEmpty();
        AvailabilityCalculator.Slots(Request(Monday, Monday.PlusDays(5), staff)).ShouldAllBe(s => s.Start.InZone(London).Date == Monday.PlusDays(5));
    }

    [Fact]
    public void A_partial_day_off_removes_only_the_slots_that_would_overlap_it()
    {
        var dentist = StaffTimeOffRules.ToIntervals(Entry(Monday, Monday, "14:00", "15:00"), London);
        var staff = AvailabilityCalculatorTests.Staff(Anna, Rota(), dentist);

        var times = LocalTimes(AvailabilityCalculator.Slots(Request(Monday, Monday, staff, serviceMinutes: 30)));

        times.ShouldContain(new LocalTime(13, 30)); // ends 14:00, touching is fine
        times.ShouldNotContain(new LocalTime(13, 45)); // would run into 14:00–15:00
        times.ShouldNotContain(new LocalTime(14, 0));
        times.ShouldNotContain(new LocalTime(14, 30));
        times.ShouldNotContain(new LocalTime(14, 45));
        times.ShouldContain(new LocalTime(15, 0)); // starts when the absence ends
        times.Count.ShouldBe(31 - 5); // 13:45, 14:00, 14:15, 14:30, 14:45 are gone
    }

    [Fact]
    public void A_partial_span_blocks_the_same_range_on_each_of_its_days()
    {
        var mornings = StaffTimeOffRules.ToIntervals(Entry(Monday, Tuesday, "09:00", "12:00"), London);
        var staff = AvailabilityCalculatorTests.Staff(Anna, Rota(), mornings);

        var slots = AvailabilityCalculator.Slots(Request(Monday, Tuesday, staff));

        slots.ShouldAllBe(s => s.Start.InZone(London).TimeOfDay >= new LocalTime(12, 0));
        slots.Count(s => s.Start.InZone(London).Date == Monday).ShouldBe(19); // 12:00 … 16:30
        slots.Count(s => s.Start.InZone(London).Date == Tuesday).ShouldBe(19);
    }

    [Fact]
    public void Time_off_and_an_appointment_add_up_as_busy_time()
    {
        var appointment = new Interval(At(Monday, 10, 0), At(Monday, 10, 30));
        var busy = StaffTimeOffRules.ToIntervals(Entry(Monday, Monday, "14:00", "17:00"), London).Append(appointment).ToArray();
        var staff = AvailabilityCalculatorTests.Staff(Anna, Rota(), busy);

        var times = LocalTimes(AvailabilityCalculator.Slots(Request(Monday, Monday, staff)));

        times.ShouldNotContain(new LocalTime(10, 0));
        times.ShouldNotContain(new LocalTime(10, 15));
        times.ShouldContain(new LocalTime(10, 30));
        times.Last().ShouldBe(new LocalTime(13, 30));
    }

    private static StaffTimeOff Entry(LocalDate from, LocalDate to, string? start = null, string? end = null)
        => new()
        {
            StaffMemberId = Anna,
            FromDate = from,
            ToDate = to,
            Start = start is null ? null : Time(start),
            End = end is null ? null : Time(end),
        };

    /// <summary>Nine to five on every day of the week.</summary>
    private static IEnumerable<KeyValuePair<IsoDayOfWeek, IReadOnlyList<TimeRange>>> Rota()
        => WorkingHoursRules.Week.Select(d => Day(d, "09:00", "17:00"));
}