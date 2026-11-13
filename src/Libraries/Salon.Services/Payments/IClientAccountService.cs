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

    /// <summary>
    /// The visit, the deposit it asks for and the deposit received for it, if any (ADR 0023). Null when the salon has no
    /// such appointment.
    /// </summary>
    Task<VisitDeposit?> GetDepositAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the salon received a deposit for the visit. <paramref name="amount"/> null takes the amount the
    /// booking asks for; a different one must be above zero and no more than the booked price. Throws
    /// <see cref="ClientAccountException"/> when the visit is unknown or no longer active, already has a deposit, the
    /// amount is out of range (or nothing was asked for and none was given), or the payment plugin refuses.
    /// </summary>
    Task<DepositLine> RecordDepositAsync(Guid appointmentId, decimal? amount, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>Records that the visit's deposit went back to the client, in full. Throws <see cref="ClientAccountException"/> when there is none to return.</summary>
    Task<DepositLine> ReturnDepositAsync(Guid appointmentId, PaymentMethod method, Guid? byUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The bill of a visit (ADR 0024): the booked price, the deposit the salon holds for it, what is left to pay, the
    /// payment once taken, and the client's open fees from earlier visits, which can be paid on the same bill. Null when
    /// the salon has no such appointment.
    /// </summary>
    Task<VisitBill?> GetBillAsync(Guid appointmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the client paid the bill with one method: what is left of the visit, and each open fee named in
    /// <paramref name="feeAppointmentIds"/> (by the visit the fee is about; unknown ids are ignored). Throws
    /// <see cref="ClientAccountException"/> when the visit is unknown, has not reached the chair (only an arrived or
    /// completed visit is paid for), there is nothing to pay, or the payment plugin refuses. The caller completes the
    /// visit; this only records money.
    /// </summary>
    Task<VisitBill> PayBillAsync(Guid appointmentId, PaymentMethod method, IReadOnlyCollection<Guid> feeAppointmentIds, Guid? byUserId, CancellationToken cancellationToken = default);



    /// <summary>What the client owes, every fee and every deposit on their account, newest first. Empty for a client without any or an unknown id.</summary>
    Task<ClientAccount> GetAccountAsync(Guid clientId, CancellationToken cancellationToken = default);
}

/// <param name="Start">In the salon's zone.</param>
/// <param name="Proposal">What the policy allows for this visit now, in full; null when it sets no fee or one is already on the account.</param>
/// <param name="DepositHeld">A deposit received for this visit and not returned (ADR 0023): the salon keeps it, so it counts toward the fee.</param>
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
    FeeLine? Fee,
    Money? DepositHeld = null)
{
    /// <summary>What is left of the proposed fee once the deposit held is taken off; null when nothing is proposed or the deposit covers it.</summary>
    public Money? Chargeable
    {
        get
        {
            if (Proposal is null)
            {
                return null;
            }

            var left = Proposal.Amount.Amount - (DepositHeld?.Amount ?? 0m);
            return left > 0m ? Proposal.Amount with { Amount = left } : null;
        }
    }

    /// <summary>The most a fee for this visit may be: the price less the deposit the salon already holds.</summary>
    public decimal MaxFee => Math.Max(0m, Price.Amount - (DepositHeld?.Amount ?? 0m));
}

/// <summary>A visit and its deposit (ADR 0023).</summary>
/// <param name="Start">In the salon's zone.</param>
/// <param name="Due">The deposit the booking asks for; null when it asks for none.</param>
/// <param name="Deposit">The deposit received, with its return if it went back; null when none was recorded.</param>
public sealed record VisitDeposit(
    Guid AppointmentId,
    Guid ClientId,
    string ClientName,
    string ServiceName,
    string StaffName,
    ZonedDateTime Start,
    AppointmentStatus Status,
    Money Price,
    Money? Due,
    DepositLine? Deposit)
{
    public bool IsActive => AppointmentStateMachine.IsActive(Status);
}

/// <summary>A deposit the salon received, with its visit and, if it went back, the return.</summary>
/// <param name="AppointmentStart">In the salon's zone.</param>
public sealed record DepositLine(
    Guid EntryId,
    Guid AppointmentId,
    Money Amount,
    Instant At,
    string? ByName,
    PaymentMethod? Method,
    string? ProviderReference,
    string ServiceName,
    ZonedDateTime AppointmentStart,
    DepositReturn? Return)
{
    /// <summary>True while the salon still has the money.</summary>
    public bool IsHeld => Return is null;
}

public sealed record DepositReturn(Instant At, string? ByName, PaymentMethod? Method, string? ProviderReference);

/// <summary>A visit as the checkout sees it (ADR 0024).</summary>
/// <param name="Start">In the salon's zone.</param>
/// <param name="DepositHeld">A deposit received for this visit and not returned: it comes off the bill.</param>
/// <param name="Payment">The payment taken for the visit; null while it is unpaid or the deposit covered it.</param>
/// <param name="OpenFees">The client's unsettled fees from other visits, oldest debt last.</param>
public sealed record VisitBill(
    Guid AppointmentId,
    Guid ClientId,
    string ClientName,
    string ServiceName,
    string StaffName,
    ZonedDateTime Start,
    AppointmentStatus Status,
    Money Price,
    Money? DepositHeld,
    VisitPaymentLine? Payment,
    IReadOnlyList<FeeLine> OpenFees)
{
    /// <summary>Only a visit the client turned up for is paid for.</summary>
    public bool CanPay => Status is AppointmentStatus.Arrived or AppointmentStatus.Completed;

    /// <summary>What is left to pay for the visit itself: nothing once it is paid, else the price less the deposit held.</summary>
    public Money Outstanding => Price with { Amount = Payment is null ? TillRules.Outstanding(Price.Amount, DepositHeld?.Amount ?? 0m) : 0m };
}

/// <summary>The payment taken for a visit at the checkout.</summary>
public sealed record VisitPaymentLine(
    Guid EntryId,
    Guid AppointmentId,
    Money Amount,
    Instant At,
    string? ByName,
    PaymentMethod? Method,
    string? ProviderReference);



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
/// <param name="Deposits">Every deposit received from the client, newest first, returned ones included.</param>
public sealed record ClientAccount(Money? Owed, IReadOnlyList<FeeLine> Fees, IReadOnlyList<DepositLine> Deposits)
{
    public static ClientAccount Empty { get; } = new(null, [], []);
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

    /// <summary>The deposit the salon holds for the visit already covers the fee the policy allows.</summary>
    CoveredByDeposit = 9,

    /// <summary>The visit already has a deposit.</summary>
    DepositAlreadyRecorded = 10,

    /// <summary>A deposit is taken for a visit that is still on; this one is finished or cancelled.</summary>
    VisitNotActive = 11,

    /// <summary>The visit has no deposit to return, or it already went back.</summary>
    NoDepositHeld = 12,

    /// <summary>Only a visit the client turned up for (arrived or completed) is paid for at the checkout.</summary>
    VisitNotPayable = 13,

    /// <summary>The visit is already paid (or covered by its deposit) and no open fee was chosen.</summary>
    NothingToPay = 14,
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