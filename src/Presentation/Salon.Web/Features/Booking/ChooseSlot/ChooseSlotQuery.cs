using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// Step 2 of the public booking flow (ADR 0014): the next days with free times for one service, for one person or for
/// anyone. <paramref name="Date"/> picks the day to list; null (or a day outside the strip) means today or the first
/// day with a free time. <paramref name="StaffMemberId"/> null means "anyone available". <paramref name="From"/> slides
/// the date strip's window towards the salon's booking horizon; null, the past or beyond the horizon means today.
/// A query: tenant required.
/// </summary>
public sealed record ChooseSlotQuery(Guid ServiceId, LocalDate? Date, Guid? StaffMemberId, LocalDate? From = null) : IQuery<ChooseSlotResult?>;

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
    DateTimeZone Zone)
{
    /// <summary>Whether the salon offers "tell me when a time comes up" (ADR 0055).</summary>
    public bool WaitlistOffered { get; init; }

    /// <summary>First day of the previous strip window, when the strip does not already start today.</summary>
    public LocalDate? EarlierFrom { get; init; }

    /// <summary>First day of the next strip window, when the salon's booking horizon reaches past this one.</summary>
    public LocalDate? LaterFrom { get; init; }

    /// <summary>The day's free times in parts of the day, in order; empty parts are left out.</summary>
    public IReadOnlyList<SlotGroup> Groups { get; init; } = [];

    /// <summary>Where that form is, with the chosen person carried along; the JoinWaitlist slice owns the route.</summary>
    public string WaitlistPath => SelectedStaffId is { } staff ? $"/book/{ServiceId}/waitlist?staff={staff}" : $"/book/{ServiceId}/waitlist";
}

/// <param name="SlotCount">How many free starts the day has — the little badge on the day tile.</param>
public sealed record DayOption(LocalDate Date, bool HasSlots, int SlotCount = 0);

/// <summary>A part of the day on the slot list ("until 12:00", "12:00–16:00", "after 16:00"); only parts with free times appear.</summary>
public sealed record SlotGroup(string LabelKey, IReadOnlyList<SlotOption> Slots);

public sealed record StaffOption(Guid Id, string DisplayName);

/// <param name="StaffMemberId">The person the engine would give this start to; informational for "anyone", the booking picks again.</param>
public sealed record SlotOption(Instant Start, LocalTime LocalStart, Guid StaffMemberId);