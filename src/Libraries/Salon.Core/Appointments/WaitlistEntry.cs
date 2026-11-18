using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Appointments;

/// <summary>
/// A client who wants a visit the diary has no room for yet (ADR 0041): which service, with whom if it matters,
/// between which days, and a remark such as "mornings only". Tenant-owned. The desk works the list from the top: the
/// one who asked first is offered a freed time first. An entry is never deleted; it is closed, with how it ended, so
/// the salon can see what became of its waiting clients.
/// </summary>
public class WaitlistEntry : BaseEntity, ITenantEntity
{
    public const int NoteMaxLength = 200;

    public Guid TenantId { get; set; }

    public Guid ClientId { get; set; }

    public Guid ServiceId { get; set; }

    /// <summary>The person the client wants; null when anyone will do.</summary>
    public Guid? StaffMemberId { get; set; }

    /// <summary>No use offering a day before this one; null means as soon as possible.</summary>
    public LocalDate? NotBefore { get; set; }

    /// <summary>No use offering a day after this one; null means any time. The entry stops waiting once the day has passed.</summary>
    public LocalDate? NotAfter { get; set; }

    /// <summary>What the desk should know when it calls: "mornings only", "before her holiday".</summary>
    public string? Note { get; set; }

    public Instant CreatedAt { get; set; }

    /// <summary>The login that put the client on the list.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>When the entry stopped waiting; null while it waits.</summary>
    public Instant? ClosedAt { get; set; }

    /// <summary>How it ended; set together with <see cref="ClosedAt"/>.</summary>
    public WaitlistOutcome? Outcome { get; set; }

    /// <summary>
    /// When the client was last sent a message that a time had come up (ADR 0056); null when never. A client is not
    /// told again for a day, so two cancellations in one afternoon are not two messages.
    /// </summary>
    public Instant? LastToldAt { get; set; }

    /// <summary>The login that took the client off the list; null when a booking did it.</summary>
    public Guid? ClosedByUserId { get; set; }

    /// <summary>The visit that ended the wait, when a booking closed the entry.</summary>
    public Guid? AppointmentId { get; set; }
}

public enum WaitlistOutcome
{
    /// <summary>The client got a visit for the service they were waiting for.</summary>
    Booked = 1,

    /// <summary>The desk took the client off the list: they no longer want it, or could not be reached.</summary>
    Removed = 2,
}

/// <summary>Who is waiting and for what (ADR 0041). Pure.</summary>
public static class WaitlistRules
{
    /// <summary>The stored form of a remark: trimmed, null when blank.</summary>
    public static string? NormalizeNote(string? note) => string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public static bool NoteFits(string? note) => note is null || note.Trim().Length <= WaitlistEntry.NoteMaxLength;

    /// <summary>The last day is not before the first; either may be left open.</summary>
    public static bool DaysFit(LocalDate? notBefore, LocalDate? notAfter) => notBefore is null || notAfter is null || notAfter >= notBefore;

    /// <summary>A last day that has already passed is a wish nobody can meet.</summary>
    public static bool IsStillPossible(LocalDate? notAfter, LocalDate today) => notAfter is null || notAfter >= today;

    /// <summary>Waiting means not closed and not past its last day.</summary>
    public static bool IsWaiting(Instant? closedAt, LocalDate? notAfter, LocalDate today) => closedAt is null && IsStillPossible(notAfter, today);

    /// <summary>The day the booking form opens on when the desk books a waiting client: the first day they can come, never in the past.</summary>
    public static LocalDate BookingDay(LocalDate? notBefore, LocalDate today) => notBefore is { } first && first > today ? first : today;

    /// <summary>A waiting client is told about a freed time at most once in this long (ADR 0056).</summary>
    public static Duration TellAgainAfter { get; } = Duration.FromHours(24);

    /// <summary>
    /// Whether a freed time is one the waiting client asked for (ADR 0056): on a day inside their days, and with the
    /// person they want when they named one.
    /// </summary>
    public static bool Wants(LocalDate? notBefore, LocalDate? notAfter, Guid? wantedStaffId, LocalDate day, Guid staffId)
        => (notBefore is null || day >= notBefore)
            && (notAfter is null || day <= notAfter)
            && (wantedStaffId is null || wantedStaffId == staffId);

    /// <summary>Whether the client may be sent a message now: never told, or told long enough ago.</summary>
    public static bool CanBeTold(Instant? lastToldAt, Instant now) => lastToldAt is null || now - lastToldAt.Value >= TellAgainAfter;
}