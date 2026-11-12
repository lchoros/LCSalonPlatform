using NodaTime;

using Salon.Core.Staff;

namespace Salon.Services.Availability;

/// <summary>
/// Everything <see cref="AvailabilityCalculator"/> needs, already loaded (ADR 0012): the salon zone, the local date range
/// (inclusive), the service duration plus the buffer kept free after it, the slot step, the clock, the booking rules and
/// the candidate staff. <see cref="MaxAdvanceDays"/> means the last bookable local day is today (in <see cref="Zone"/>)
/// plus that many days.
/// </summary>
public sealed record AvailabilityRequest(
    DateTimeZone Zone,
    LocalDate From,
    LocalDate To,
    Duration ServiceDuration,
    Duration Buffer,
    int StepMinutes,
    Instant Now,
    Duration MinNotice,
    int MaxAdvanceDays,
    IReadOnlyList<StaffAvailabilityInput> Staff);

/// <summary>
/// One candidate staff member: the weekly rota (ADR 0010, local times, several ranges per day for split shifts) and the
/// intervals already taken (active appointments including their buffer, and time off as one interval per day, ADR 0015).
/// </summary>
/// <param name="ServiceDuration">This person's own duration for the service (ADR 0018); null means <see cref="AvailabilityRequest.ServiceDuration"/>.</param>
public sealed record StaffAvailabilityInput(
    Guid StaffMemberId,
    IReadOnlyDictionary<IsoDayOfWeek, IReadOnlyList<TimeRange>> WorkingRanges,
    IReadOnlyList<Interval> Busy,
    Duration? ServiceDuration = null);