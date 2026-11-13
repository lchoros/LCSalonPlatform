using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NodaTime;
using Salon.Core.Staff;
using Salon.Services.Availability;
using Interval = NodaTime.Interval;

namespace Salon.Tests.Unit.Availability;

/// <summary>
/// The invariants ADR 0012 demands of the engine, over random rotas, busy intervals, steps, buffers and both salon zones.
/// Working ranges stay between 05:00 and 23:00 so a range never spans the night-time DST change (that edge is covered by
/// the example tests); the dates themselves include both 2026 DST change weekends.
/// </summary>
public class AvailabilityCalculatorPropertyTests
{
    private static readonly DateTimeZone[] Zones = [DateTimeZoneProviders.Tzdb["Europe/London"], DateTimeZoneProviders.Tzdb["Europe/Warsaw"]];

    /// <summary>Clock steps that divide the hour, as a salon would configure.</summary>
    private static readonly Gen<int> StepGen = Gen.Elements(5, 10, 15, 20, 30, 60);

    private static readonly Gen<DateTimeZone> ZoneGen = Gen.Elements(Zones);

    /// <summary>Six weeks around each 2026 DST change plus a plain summer fortnight.</summary>
    private static readonly Gen<LocalDate> DateGen = Gen.OneOf(
        Gen.Choose(0, 20).Select(d => new LocalDate(2026, 3, 20).PlusDays(d)),
        Gen.Choose(0, 20).Select(d => new LocalDate(2026, 10, 15).PlusDays(d)),
        Gen.Choose(0, 13).Select(d => new LocalDate(2026, 6, 1).PlusDays(d)));

    /// <summary>A working range on the 5-minute grid between 05:00 and 23:00, at least 30 minutes long.</summary>
    private static readonly Gen<TimeRange> RangeGen =
        from start in Gen.Choose(5 * 12, (23 * 12) - 6)
        from length in Gen.Choose(6, (23 * 12) - start)
        select new TimeRange(LocalTime.Midnight.PlusMinutes(start * 5), LocalTime.Midnight.PlusMinutes((start + length) * 5));

    /// <summary>Up to two non-overlapping ranges per day (ADR 0010), for a random subset of the week.</summary>
    private static readonly Gen<IReadOnlyDictionary<IsoDayOfWeek, IReadOnlyList<TimeRange>>> RotaGen =
        Gen.ArrayOf(Gen.Zip(Gen.Elements(WorkingHoursRules.Week.ToArray()), Gen.ArrayOf(RangeGen).Select(NonOverlapping)))
            .Select(pairs => (IReadOnlyDictionary<IsoDayOfWeek, IReadOnlyList<TimeRange>>)pairs
                .Where(p => p.Item2.Count > 0)
                .GroupBy(p => p.Item1)
                .ToDictionary(g => g.Key, g => g.First().Item2));

    [Property(MaxTest = 200)]
    public Property No_slot_overlaps_a_busy_interval_even_with_its_buffer()
    {
        return Prop.ForAll(RequestGen().ToArbitrary(), request =>
        {
            var busyByStaff = request.Staff.ToDictionary(s => s.StaffMemberId, s => s.Busy);
            return AvailabilityCalculator.Slots(request).All(slot =>
                busyByStaff[slot.StaffMemberId].All(busy => !Overlaps(busy, new Interval(slot.Start, slot.End.Plus(request.Buffer)))));
        });
    }

    [Property(MaxTest = 200)]
    public Property Every_slot_with_its_buffer_lies_inside_one_working_range_of_its_staff_member()
    {
        return Prop.ForAll(RequestGen().ToArbitrary(), request =>
        {
            var rotas = request.Staff.ToDictionary(s => s.StaffMemberId, s => s.WorkingRanges);
            return AvailabilityCalculator.Slots(request).All(slot =>
            {
                var start = slot.Start.InZone(request.Zone).LocalDateTime;
                var end = slot.End.Plus(request.Buffer).InZone(request.Zone).LocalDateTime;
                return start.Date >= request.From
                    && start.Date <= request.To
                    && rotas[slot.StaffMemberId].TryGetValue(start.DayOfWeek, out var ranges)
                    && ranges.Any(r => start.TimeOfDay >= r.Start && end.Date == start.Date && end.TimeOfDay <= r.End);
            });
        });
    }

    [Property(MaxTest = 200)]
    public Property Slots_start_on_the_clock_step_last_the_service_duration_and_are_ordered()
    {
        return Prop.ForAll(RequestGen().ToArbitrary(), request =>
        {
            var slots = AvailabilityCalculator.Slots(request);
            var aligned = slots.All(s =>
            {
                var local = s.Start.InZone(request.Zone).TimeOfDay;
                return local.Second == 0 && ((local.Hour * 60) + local.Minute) % request.StepMinutes == 0;
            });
            var duration = slots.All(s => s.End - s.Start == request.ServiceDuration);
            var ordered = slots.Zip(slots.Skip(1)).All(pair => pair.First.Start <= pair.Second.Start);
            var notice = slots.All(s => s.Start >= request.Now.Plus(request.MinNotice));
            return aligned && duration && ordered && notice;
        });
    }

    [Property(MaxTest = 200)]
    public Property Adding_a_busy_interval_never_adds_a_slot()
    {
        var gen =
            from request in RequestGen().Where(r => r.Staff.Count > 0)
            from index in Gen.Choose(0, request.Staff.Count - 1)
            from busy in BusyGen(request.Zone, request.From, request.To)
            select (Request: request, Index: index, Busy: busy);

        return Prop.ForAll(gen.ToArbitrary(), input =>
        {
            var before = AvailabilityCalculator.Slots(input.Request).ToHashSet();
            var staff = input.Request.Staff.ToList();
            staff[input.Index] = staff[input.Index] with { Busy = [.. staff[input.Index].Busy, input.Busy] };

            var after = AvailabilityCalculator.Slots(input.Request with { Staff = staff });

            return after.All(before.Contains);
        });
    }

    [Property(MaxTest = 200)]
    public Property Any_staff_is_the_union_of_per_staff_results_with_one_slot_per_start()
    {
        return Prop.ForAll(RequestGen().ToArbitrary(), request =>
        {
            var perStaff = request.Staff
                .SelectMany(s => AvailabilityCalculator.Slots(request with { Staff = [s] }))
                .ToHashSet();
            var any = AvailabilityCalculator.ForAnyStaff(request);

            var sameStarts = any.Select(s => s.Start).ToHashSet().SetEquals(perStaff.Select(s => s.Start));
            var oneEach = any.Select(s => s.Start).Distinct().Count() == any.Count;
            var fromPerStaff = any.All(perStaff.Contains);
            var ordered = any.Zip(any.Skip(1)).All(pair => pair.First.Start < pair.Second.Start);
            return sameStarts && oneEach && fromPerStaff && ordered;
        });
    }

    [Property(MaxTest = 100)]
    public Property The_engine_is_deterministic()
    {
        return Prop.ForAll(RequestGen().ToArbitrary(), request =>
            AvailabilityCalculator.Slots(request).SequenceEqual(AvailabilityCalculator.Slots(request)));
    }

    /// <summary>ADR 0018: a staff-specific duration replaces the service duration for that person's slots and nobody else's.</summary>
    [Property(MaxTest = 200)]
    public Property Every_slot_lasts_the_effective_duration_of_its_staff_member()
    {
        var gen =
            from request in RequestGen()
            from overrides in Gen.ArrayOf(Gen.OneOf(Gen.Constant((Duration?)null), Gen.Elements(10, 20, 30, 40, 50, 60, 75, 90, 120).Select(m => (Duration?)Duration.FromMinutes(m))), request.Staff.Count)
            select request with { Staff = request.Staff.Select((s, i) => s with { ServiceDuration = overrides[i] }).ToArray() };

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var effective = request.Staff.ToDictionary(s => s.StaffMemberId, s => s.ServiceDuration ?? request.ServiceDuration);
            var perStaff = AvailabilityCalculator.Slots(request);
            var any = AvailabilityCalculator.ForAnyStaff(request);
            return perStaff.All(s => s.End - s.Start == effective[s.StaffMemberId])
                && any.All(s => s.End - s.Start == effective[s.StaffMemberId])
                && perStaff.All(s => !request.Staff.Single(x => x.StaffMemberId == s.StaffMemberId).Busy.Any(b => Overlaps(b, new Interval(s.Start, s.End.Plus(request.Buffer)))));
        });
    }

    private static Gen<AvailabilityRequest> RequestGen()
        => from zone in ZoneGen
           from firstDay in DateGen
           from days in Gen.Choose(0, 6)
           from serviceMinutes in Gen.Elements(15, 20, 30, 45, 60, 90)
           from bufferMinutes in Gen.Elements(0, 0, 5, 10, 15)
           from step in StepGen
           from nowOffsetHours in Gen.Choose(-72, 72)
           from noticeMinutes in Gen.Elements(0, 30, 60, 120)
           from maxAdvance in Gen.Elements(1, 3, 7, 60)
           from staffCount in Gen.Choose(0, 3)
           from staff in Gen.ArrayOf(StaffGen(zone, firstDay, firstDay.PlusDays(days)), staffCount)
           select new AvailabilityRequest(
               zone,
               firstDay,
               firstDay.PlusDays(days),
               Duration.FromMinutes(serviceMinutes),
               Duration.FromMinutes(bufferMinutes),
               step,
               zone.AtStartOfDay(firstDay).ToInstant().Plus(Duration.FromHours(nowOffsetHours)),
               Duration.FromMinutes(noticeMinutes),
               maxAdvance,
               staff.Select((s, i) => s with { StaffMemberId = Guid.Parse($"00000000-0000-0000-0000-{i + 1:D12}") }).ToArray());

    private static Gen<StaffAvailabilityInput> StaffGen(DateTimeZone zone, LocalDate firstDay, LocalDate lastDay)
        => from rota in RotaGen
           from busyCount in Gen.Choose(0, 4)
           from busy in Gen.ArrayOf(BusyGen(zone, firstDay, lastDay), busyCount)
           select new StaffAvailabilityInput(Guid.Empty, rota, busy);

    /// <summary>A busy interval of 5 minutes to 3 hours starting on the 5-minute grid somewhere in the requested days.</summary>
    private static Gen<Interval> BusyGen(DateTimeZone zone, LocalDate firstDay, LocalDate lastDay)
        => from dayOffset in Gen.Choose(0, Period.Between(firstDay, lastDay, PeriodUnits.Days).Days)
           from startMinute in Gen.Choose(0, (24 * 12) - 1)
           from length in Gen.Choose(1, 36)
           let start = zone.AtLeniently(firstDay.PlusDays(dayOffset).AtMidnight().PlusMinutes(startMinute * 5)).ToInstant()
           select new Interval(start, start.Plus(Duration.FromMinutes(length * 5)));

    private static IReadOnlyList<TimeRange> NonOverlapping(TimeRange[] ranges)
    {
        var kept = new List<TimeRange>();
        foreach (var range in ranges.OrderBy(r => r.Start))
        {
            if (kept.Count < WorkingHoursRules.MaxRangesPerDay && kept.All(k => !k.Overlaps(range)))
            {
                kept.Add(range);
            }
        }

        return kept;
    }

    private static bool Overlaps(Interval a, Interval b) => a.Start < b.End && b.Start < a.End;
}