using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Domain;
using Salon.Core.Payments;

namespace Salon.Services.Payments;

/// <summary>
/// A client's account with the salon (ADR 0022): the fee a missed or late-cancelled visit may carry, adding it, and
/// settling it by a payment taken at the salon or by a waiver. The cancellation policy only proposes; every line is added
/// by a person and names them. Lines are never changed or removed. Payments go through the salon's
/// <see cref="IPaymentProvider"/>, which in Phase 1 only records that the salon took the money itself. Called from a
/// command, everything shares the pipeline's transaction (ADR 0003).
/// </summary>
public interface IClientAccountService
{
    /// <summary>
    /// The visit, the fee the policy allows for it now and the fee already on the account, if any. Null when the salon has
    /// no such appointment.
    /// </summary>
    Task<AppointmentFee?> GetFeeAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the fee the policy proposes for the visit to the client's account. <paramref name="amount"/> null takes the
    /// proposed amount; a different one must be above zero and no more than the booked price. Throws
    /// <see cref="ClientAccountException"/> when the visit is unknown, carries no fee under the policy, already has one, or
    /// the amount is out of range.
    /// </summary>
    Task<FeeLine> ChargeFeeAsync(Guid appointmentId, decimal? amount, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>Lets the visit's fee go, in full, with the reason. Throws <see cref="ClientAccountException"/> when there is no open fee or no reason.</summary>
    Task<FeeLine> WaiveFeeAsync(Guid appointmentId, string? reason, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the client paid the visit's fee, in full, at the salon. Throws <see cref="ClientAccountException"/> when
    /// there is no open fee or the payment plugin refuses to record it.
    /// </summary>
    Task<FeeLine> RecordFeePaymentAsync(Guid appointmentId, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>What the client owes and every fee on their account, newest first. Empty for a client without fees or an unknown id.</summary>
    Task<ClientAccount> GetAccountAsync(Guid clientId, CancellationToken cancellationToken = default);
}

/// <param name="Start">In the salon's zone.</param>
/// <param name="Proposal">What the policy allows for this visit now; null when it sets no fee or one is already on the account.</param>
/// <param name="Fee">The fee on the account, with how it was settled; null when none was added.</param>
public sealed record AppointmentFee(
    Guid AppointmentId,
    Guid ClientId,
    string ClientName,
    string ServiceName,
    string StaffName,
    ZonedDateTime Start,
    AppointmentStatus Status,
    Money Price,
    FeeProposal? Proposal,
    FeeLine? Fee);

public enum FeeStatus
{
    Owed = 0,
    Paid = 1,
    Waived = 2,
}

/// <summary>A fee with its visit and its settlement: one row of the account as people read it.</summary>
/// <param name="AppointmentStart">In the salon's zone.</param>
/// <param name="ByName">Display name of the login that added the fee; null when it is gone or unknown.</param>
public sealed record FeeLine(
    Guid EntryId,
    Guid AppointmentId,
    ClientAccountEntryKind Kind,
    Money Amount,
    Instant At,
    string? ByName,
    string ServiceName,
    ZonedDateTime AppointmentStart,
    FeeSettlement? Settlement)
{
    public FeeStatus Status => Settlement switch
    {
        null => FeeStatus.Owed,
        { Kind: ClientAccountEntryKind.FeePayment } => FeeStatus.Paid,
        _ => FeeStatus.Waived,
    };
}

/// <param name="Kind"><see cref="ClientAccountEntryKind.FeePayment"/> or <see cref="ClientAccountEntryKind.FeeWaived"/>.</param>
/// <param name="Method">How it was paid; null for a waiver.</param>
/// <param name="Note">Why it was waived; null for a payment.</param>
/// <param name="ProviderReference">The payment plugin's reference; null for a waiver.</param>
public sealed record FeeSettlement(
    ClientAccountEntryKind Kind,
    Instant At,
    string? ByName,
    PaymentMethod? Method,
    string? Note,
    string? ProviderReference);

/// <param name="Owed">The open fees added up; null when the client owes nothing.</param>
public sealed record ClientAccount(Money? Owed, IReadOnlyList<FeeLine> Fees)
{
    public static ClientAccount Empty { get; } = new(null, []);
}

public enum ClientAccountRefusal
{
    /// <summary>The salon has no such appointment.</summary>
    NotFound = 1,

    /// <summary>The policy sets no fee for this visit (it took place, was cancelled in time, or the percentage is zero).</summary>
    NoFeeUnderPolicy = 2,

    /// <summary>The visit already has a fee on the account.</summary>
    AlreadyCharged = 3,

    /// <summary>The amount is not above zero or is more than the booked price.</summary>
    AmountOutOfRange = 4,

    /// <summary>The visit has no fee to settle.</summary>
    NoFee = 5,

    /// <summary>The fee has already been paid or waived.</summary>
    AlreadySettled = 6,

    /// <summary>A waiver needs a reason.</summary>
    ReasonRequired = 7,

    /// <summary>The payment plugin did not record the payment.</summary>
    PaymentNotRecorded = 8,
}

/// <summary>A request the account refuses; <see cref="Refusal"/> says why, for the message the screen shows.</summary>
public sealed class ClientAccountException : Exception
{
    public ClientAccountException()
        : this(ClientAccountRefusal.NotFound)
    {
    }

    public ClientAccountException(string message)
        : base(message)
    {
    }

    public ClientAccountException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ClientAccountException(ClientAccountRefusal refusal, Exception? innerException = null)
        : base($"The client account refused the request: {refusal}.", innerException)
    {
        Refusal = refusal;
    }

    public ClientAccountRefusal Refusal { get; } = ClientAccountRefusal.NotFound;
}