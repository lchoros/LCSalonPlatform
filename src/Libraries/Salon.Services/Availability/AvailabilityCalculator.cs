using NodaTime;

using Salon.Core.Staff;

namespace Salon.Services.Availability;

/// <summary>
/// The pure availability engine (ADR 0012). A slot starts on a step boundary of the clock inside a working range, the
/// whole <c>[start, end + buffer)</c> lies inside that one range and overlaps no busy interval, the start is no earlier
/// than <c>now + MinNotice</c> and no later than the last bookable day. Local times become instants with
/// <see cref="DateTimeZone.AtLeniently"/>, so a rota that spans a DST gap or overlap never throws; starts that collapse onto
/// the same instant in a gap are reported once.
/// </summary>
public static class AvailabilityCalculator
{
    /// <summary>Every slot of every candidate staff member, ordered by start, then by the staff order in the request.</summary>
    public static IReadOnlyList<Slot> Slots(AvailabilityRequest request)
    {
        Validate(request);

        var slots = new List<Slot>();
        foreach (var staff in request.Staff)
        {
            slots.AddRange(SlotsFor(request, staff));
        }

        return [.. slots.OrderBy(s => s.Start).ThenBy(s => IndexOf(request, s.StaffMemberId))];
    }

    /// <summary>
    /// "Any staff": the union of every staff member's slots de-duplicated by start. A start several people can take goes to
    /// the one with the fewest busy intervals on that local day; a tie goes to the staff member listed first.
    /// </summary>
    public static IReadOnlyList<Slot> ForAnyStaff(AvailabilityRequest request)
    {
        var all = Slots(request);
        var busyByStaffAndDay = new Dictionary<(Guid StaffMemberId, LocalDate Day), int>();
        foreach (var staff in request.Staff)
        {
            foreach (var busy in staff.Busy)
            {
                var key = (staff.StaffMemberId, busy.Start.InZone(request.Zone).Date);
                busyByStaffAndDay[key] = busyByStaffAndDay.GetValueOrDefault(key) + 1;
            }
        }

        return
        [
            .. all
                .GroupBy(s => s.Start)
                .OrderBy(g => g.Key)
                .Select(g => g
                    .OrderBy(s => busyByStaffAndDay.GetValueOrDefault((s.StaffMemberId, s.Start.InZone(request.Zone).Date)))
                    .ThenBy(s => IndexOf(request, s.StaffMemberId))
                    .First()),
        ];
    }

    private static IEnumerable<Slot> SlotsFor(AvailabilityRequest request, StaffAvailabilityInput staff)
    {
        var earliestStart = request.Now.Plus(request.MinNotice);
        var lastDay = request.Now.InZone(request.Zone).Date.PlusDays(request.MaxAdvanceDays);
        var occupiedTicks = request.ServiceDuration.Plus(request.Buffer).BclCompatibleTicks;
        var seen = new HashSet<Instant>();

        for (var day = request.From; day <= request.To && day <= lastDay; day = day.PlusDays(1))
        {
            if (!staff.WorkingRanges.TryGetValue(day.DayOfWeek, out var ranges))
            {
                continue;
            }

            foreach (var range in ranges.OrderBy(r => r.Start))
            {
                var rangeEnd = day.At(range.End);
                for (var start = FirstStepAtOrAfter(day, range.Start, request.StepMinutes); start.PlusTicks(occupiedTicks) <= rangeEnd; start = start.PlusMinutes(request.StepMinutes))
                {
                    var startInstant = request.Zone.AtLeniently(start).ToInstant();
                    if (startInstant < earliestStart || !seen.Add(startInstant))
                    {
                        continue;
                    }

                    var endInstant = startInstant.Plus(request.ServiceDuration);
                    var held = new Interval(startInstant, endInstant.Plus(request.Buffer));
                    if (staff.Busy.Any(b => Overlaps(b, held)))
                    {
                        continue;
                    }

                    yield return new Slot(staff.StaffMemberId, startInstant, endInstant);
                }
            }
        }
    }

    /// <summary>The first clock-aligned step (minute of day divisible by the step) at or after the range start.</summary>
    private static LocalDateTime FirstStepAtOrAfter(LocalDate day, LocalTime rangeStart, int stepMinutes)
    {
        var minuteOfDay = (rangeStart.Hour * 60) + rangeStart.Minute;
        var hasRemainder = rangeStart.Second != 0 || rangeStart.NanosecondOfSecond != 0 || minuteOfDay % stepMinutes != 0;
        var aligned = hasRemainder ? ((minuteOfDay / stepMinutes) + 1) * stepMinutes : minuteOfDay;
        return day.AtMidnight().PlusMinutes(aligned);
    }

    private static bool Overlaps(Interval a, Interval b) => a.Start < b.End && b.Start < a.End;

    private static int IndexOf(AvailabilityRequest request, Guid staffMemberId)
    {
        for (var i = 0; i < request.Staff.Count; i++)
        {
            if (request.Staff[i].StaffMemberId == staffMemberId)
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private static void Validate(AvailabilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Zone);
        ArgumentNullException.ThrowIfNull(request.Staff);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.StepMinutes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.ServiceDuration, Duration.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Buffer, Duration.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MinNotice, Duration.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(request.MaxAdvanceDays);
        if (request.To < request.From)
        {
            throw new ArgumentException("The date range ends before it starts.", nameof(request));
        }
    }
}