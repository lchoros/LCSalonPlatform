using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Appointments;

/// <summary>
/// A freed time held for one waiting client (ADR 0065): the time of a cancelled visit, with the person who would do
/// it, kept off the booking pages until <see cref="HeldUntil"/> so that the client who was told about it can take it
/// with one tap. Tenant-owned. An offer is never deleted while its waitlist entry stands: it ends, with how it ended,
/// so the salon can see who was offered what.
/// </summary>
public class WaitlistOffer : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>Whose wish this time answers; the client is the entry's.</summary>
    public Guid WaitlistEntryId { get; set; }

    public Guid ServiceId { get; set; }

    public Guid StaffMemberId { get; set; }

    public Instant Start { get; set; }

    /// <summary>Start plus the time this person takes for the service, as the booking pages count it.</summary>
    public Instant End { get; set; }

    /// <summary>The secret of the link in the client's text: whoever has it may take the time.</summary>
    public required string Token { get; set; }

    public Instant OfferedAt { get; set; }

    /// <summary>The time is kept for the client until this moment; afterwards anybody may book it.</summary>
    public Instant HeldUntil { get; set; }

    /// <summary>When the offer stopped standing; null while it stands (even past <see cref="HeldUntil"/>, until the clock passes it on).</summary>
    public Instant? EndedAt { get; set; }

    /// <summary>How it ended; set together with <see cref="EndedAt"/>.</summary>
    public WaitlistOfferEnd? Outcome { get; set; }

    /// <summary>The visit the client booked from this offer.</summary>
    public Guid? AppointmentId { get; set; }

    /// <summary>The cancelled visit whose time this is, so its own client is not offered it back.</summary>
    public Guid? FreedByAppointmentId { get; set; }

    /// <summary>The login that let the time go; null when the client or the clock ended the offer.</summary>
    public Guid? EndedByUserId { get; set; }
}

public enum WaitlistOfferEnd
{
    /// <summary>The client took the time: a visit was booked.</summary>
    Taken = 1,

    /// <summary>The hold ran out, or the client stopped waiting; the time went to the next in line or back on the booking pages.</summary>
    NotTaken = 2,

    /// <summary>The desk let the time go by hand.</summary>
    Released = 3,

    /// <summary>The client tried to take it and the time could not be booked any more.</summary>
    Gone = 4,
}

/// <summary>Where an offer stands as its client sees it.</summary>
public enum WaitlistOfferState
{
    /// <summary>The time is kept for the client and can be taken now.</summary>
    Held = 1,

    /// <summary>The client booked it.</summary>
    Taken = 2,

    /// <summary>It is no longer kept for them: it ran out, was let go, or could not be booked.</summary>
    Over = 3,
}

/// <summary>How long a freed time is held, and whether it still is (ADR 0065). Pure.</summary>
public static class WaitlistHoldRules
{
    /// <summary>Four hours: longer than that and a time freed in the morning is still empty in the afternoon.</summary>
    public const int MaxHoldMinutes = 240;

    /// <summary>Zero means "do not hold": the text goes out and whoever books first has the time (ADR 0056).</summary>
    public static bool IsHoldMinutes(int minutes) => minutes is >= 0 and <= MaxHoldMinutes;

    /// <summary>
    /// Until when the time is kept: so many minutes from now, but never beyond the last moment the time can still be
    /// booked online (its start less the salon's notice), so a hold never outlives what it holds.
    /// </summary>
    public static Instant HeldUntil(Instant now, int holdMinutes, Instant start, int minNoticeMinutes)
    {
        var wanted = now + Duration.FromMinutes(holdMinutes);
        var lastBookable = start - Duration.FromMinutes(minNoticeMinutes);
        return wanted < lastBookable ? wanted : lastBookable;
    }

    /// <summary>Whether the time is kept off the booking pages right now.</summary>
    public static bool IsHeld(Instant? endedAt, Instant heldUntil, Instant now) => endedAt is null && heldUntil > now;

    public static WaitlistOfferState State(Instant? endedAt, WaitlistOfferEnd? outcome, Instant heldUntil, Instant now)
        => outcome == WaitlistOfferEnd.Taken ? WaitlistOfferState.Taken
            : IsHeld(endedAt, heldUntil, now) ? WaitlistOfferState.Held
            : WaitlistOfferState.Over;
}