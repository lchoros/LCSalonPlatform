using NodaTime;

namespace Salon.Services.ClientPortal;

/// <summary>
/// "My bookings" for a salon's client (ADR 0083): no password and no account. The client proves a mobile number with
/// the same one-time text message code a booking uses (ADR 0014) and may then see the visits still ahead of them at
/// this salon. Nothing here says whether a number is known: asking for a code looks the same either way, and a code
/// that does not open the door is refused in one wording whatever the reason. Everything reads through the tenant
/// filter, so another salon's rows do not exist here.
/// </summary>
public interface IClientPortalService
{
    /// <summary>
    /// Stores a row for the number and, only when the salon has a client card with it, texts the code. The answer is
    /// the row's id in both cases; a number nobody knows gets a row that no code can ever prove. Throws
    /// <see cref="ArgumentException"/> for a number that cannot be read and
    /// <see cref="Booking.PhoneVerificationRateLimitedException"/> after
    /// <see cref="Core.Booking.PhoneVerificationRules.MaxCodesPerPhone"/> codes to one number in an hour, counted
    /// together with booking codes and whether or not the number is known.
    /// </summary>
    Task<Guid> RequestCodeAsync(string? phone, CancellationToken cancellationToken = default);

    /// <summary>What the code page shows; null when the id is not a sign-in of this salon.</summary>
    Task<ClientPortalPendingCode?> FindPendingAsync(Guid verificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the code. A wrong one counts an attempt; a row that is expired, used up or already spent never opens the
    /// door. The caller is told only <see cref="ClientPortalSignInOutcome.Refused"/> for all of these.
    /// </summary>
    Task<ClientPortalSignIn> VerifyAsync(Guid verificationId, string? code, CancellationToken cancellationToken = default);

    /// <summary>
    /// The visits that have not started yet and still hold their time, of every client card that carries the number
    /// of card <paramref name="clientId"/>, soonest first. Null when the session no longer stands: the card is gone,
    /// erased, merged away, has no number, or was changed after <paramref name="signedInAt"/> (its number may no
    /// longer be the one that was proven).
    /// </summary>
    Task<IReadOnlyList<ClientPortalVisit>?> GetUpcomingVisitsAsync(Guid clientId, Instant signedInAt, CancellationToken cancellationToken = default);
}

/// <param name="PhoneE164">The number that was typed; the page masks it.</param>
public sealed record ClientPortalPendingCode(Guid Id, string PhoneE164)
{
    /// <summary>Keeps the phone out of any log line that prints the record.</summary>
    public override string ToString() => $"{nameof(ClientPortalPendingCode)} {{ Id = {Id} }}";
}

public enum ClientPortalSignInOutcome
{
    SignedIn = 1,

    /// <summary>Wrong, expired, used up, spent, or the number has no card: one answer for all of them.</summary>
    Refused = 2,

    /// <summary>The id is not a sign-in of this salon.</summary>
    NotFound = 3,
}

/// <param name="ClientId">The card the session hangs on; set only when signed in.</param>
public sealed record ClientPortalSignIn(ClientPortalSignInOutcome Outcome, Guid? ClientId = null);

/// <summary>
/// What a client may see about a visit ahead of them: what, with whom and when. No price, no note, no name of
/// anybody else.
/// </summary>
/// <param name="PublicToken">The secret of the client's own change and cancel pages (ADR 0014); null on a visit booked at the desk before ADR 0029.</param>
public sealed record ClientPortalVisit(Guid AppointmentId, string ServiceName, string StaffName, Instant Start, Instant End, string? PublicToken);