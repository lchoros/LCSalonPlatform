using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// Step 2 of the public booking flow (ADR 0014): the next days with free times for one service, for one person or for
/// anyone. <paramref name="Date"/> picks the day to list; null (or a day outside the strip) means today or the first
/// day with a free time. <paramref name="StaffMemberId"/> null means "anyone available". A query: tenant required.
/// </summary>
public sealed record ChooseSlotQuery(Guid ServiceId, LocalDate? Date, Guid? StaffMemberId) : IQuery<ChooseSlotResult?>;

/// <param name="Days">The date strip, in order; <see cref="DayOption.HasSlots"/> marks days worth tapping.</param>
/// <param name="Slots">Free starts on <see cref="SelectedDate"/>, in order; for "anyone" one per start.</param>
/// <param name="Zone">The salon's zone: times are shown in it (CLAUDE.md rule 5).</param>
public sealed record ChooseSlotResult(
    Guid ServiceId,
    string ServiceName,
    Duration Duration,
    Money Price,
    bool PriceIsFrom,
    IReadOnlyList<DayOption> Days,
    LocalDate SelectedDate,
    IReadOnlyList<StaffOption> Staff,
    Guid? SelectedStaffId,
    IReadOnlyList<SlotOption> Slots,
    DateTimeZone Zone);

public sealed record DayOption(LocalDate Date, bool HasSlots);

public sealed record StaffOption(Guid Id, string DisplayName);

/// <param name="StaffMemberId">The person the engine would give this start to; informational for "anyone", the booking picks again.</param>
public sealed record SlotOption(Instant Start, LocalTime LocalStart, Guid StaffMemberId);