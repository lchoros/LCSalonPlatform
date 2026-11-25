using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// Step 2 of the public booking flow (ADR 0014): the next days with free times for one service, for one person or for
/// anyone. <paramref name="Date"/> picks the day to list; null (or a day outside the strip) means today or the first
/// day with a free time. <paramref name="StaffMemberId"/> null with <paramref name="Anyone"/> means
/// "anyone available"; null without it means the visitor has not said yet, and where more than one person offers the
/// service no times are shown until they do (the owner, 2026-11-22). <paramref name="From"/> slides
/// the date strip's window towards the salon's booking horizon; null, the past or beyond the horizon means today.
/// <paramref name="Start"/> is the tapped time: it marks the slot chosen and brings out the Continue button, and only
/// a start that is genuinely free right now counts (a stale link quietly deselects). A query: tenant required.
/// </summary>
public sealed record ChooseSlotQuery(Guid ServiceId, LocalDate? Date, Guid? StaffMemberId, LocalDate? From = null, Instant? Start = null, bool Anyone = false, IReadOnlyList<Guid>? ExtraServiceIds = null) : IQuery<ChooseSlotResult?>
{
    /// <summary>The <c>staff</c> value in the address that says "anyone available" was chosen.</summary>
    public const string AnyStaff = "any";
}

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
    /// <summary>
    /// More than one person offers the service and the visitor has not said who (or "anyone") yet. The days and times
    /// shown are then those of anyone who could do it, and Continue asks for the answer first.
    /// </summary>
    public bool NeedsStaffChoice { get; init; }

    /// <summary>The address asked for <see cref="SelectedDate"/> itself (a link from a message does): the answer to "who with" keeps it.</summary>
    public bool DayAsked { get; init; }

    /// <summary>"Anyone available" was chosen, as against nothing chosen yet.</summary>
    public bool AnyoneChosen { get; init; }

    /// <summary>
    /// The services of the visit in order (ADR 0095): one for most bookings. <see cref="ServiceName"/> is the first;
    /// <see cref="Duration"/> and <see cref="Price"/> are the sums.
    /// </summary>
    public IReadOnlyList<VisitLine> Lines { get; init; } = [];

    /// <summary>Several services were chosen and no one person offers them all: there is nothing to book until one is taken out.</summary>
    public bool NobodyOffersAll { get; init; }

    /// <summary>Whether the salon offers "tell me when a time comes up" (ADR 0055).</summary>
    public bool WaitlistOffered { get; init; }

    /// <summary>First day of the previous strip window, when the strip does not already start today.</summary>
    public LocalDate? EarlierFrom { get; init; }

    /// <summary>First day of the next strip window, when the salon's booking horizon reaches past this one.</summary>
    public LocalDate? LaterFrom { get; init; }

    /// <summary>The day's free times in parts of the day, in order; empty parts are left out.</summary>
    public IReadOnlyList<SlotGroup> Groups { get; init; } = [];

    /// <summary>The tapped time, when it is one of <see cref="Slots"/>; the Continue button appears with it.</summary>
    public SlotOption? Chosen { get; init; }

    /// <summary>Which of <see cref="Groups"/> holds the chosen time (0 when none): the part-of-day tab a phone opens on.</summary>
    public int ChosenGroup { get; init; }

    /// <summary>Where that form is, with the chosen person carried along; the JoinWaitlist slice owns the route.</summary>
    public string WaitlistPath => SelectedStaffId is { } staff ? $"/book/{ServiceId}/waitlist?staff={staff}" : $"/book/{ServiceId}/waitlist";
}

/// <summary>One service of the visit as the summary shows it; "from" while nobody is chosen and people charge differently.</summary>
public sealed record VisitLine(Guid ServiceId, string Name, Duration Duration, Money Price, bool PriceIsFrom);

public sealed record DayOption(LocalDate Date, bool HasSlots);

/// <summary>A part of the day on the slot list ("until 12:00", "12:00–16:00", "after 16:00"); only parts with free times appear.</summary>
public sealed record SlotGroup(string LabelKey, IReadOnlyList<SlotOption> Slots);

public sealed record StaffOption(Guid Id, string DisplayName)
{
    /// <summary>The address of the person's picture (ADR 0088); null shows the first letter of the name.</summary>
    public string? PhotoPath { get; init; }
}

/// <param name="StaffMemberId">The person the engine would give this start to; informational for "anyone", the booking picks again.</param>
public sealed record SlotOption(Instant Start, LocalTime LocalStart, Guid StaffMemberId);