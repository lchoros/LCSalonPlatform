using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Settings;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// What the details page shows above the form (ADR 0014): the chosen service, person ("anyone" when null) and start, in
/// the salon's zone. Null when the service is unknown or inactive, or the person is not bookable here. A query.
/// </summary>
public sealed record BookingSummaryQuery(Guid ServiceId, Guid? StaffMemberId, Instant Start) : IQuery<BookingSummary?>;

/// <param name="StaffName">Null for "anyone available".</param>
/// <param name="RequireSmsVerification">Decides the wording of the submit button and the phone hint.</param>
/// <param name="Policy">The salon's cancellation policy (ADR 0022), shown before the client commits.</param>
/// <param name="Deposit">The deposit this service asks for at the price shown (ADR 0023); null when none. The client's own rule applies once they are known.</param>
public sealed record BookingSummary(
    string ServiceName,
    Duration Duration,
    Money Price,
    bool PriceIsFrom,
    string? StaffName,
    Instant Start,
    DateTimeZone Zone,
    string SalonName,
    string ConsentWording,
    bool RequireSmsVerification,
    CancellationPolicySettings Policy,
    Money? Deposit = null)
{
    /// <summary>True when a payment gateway takes the deposit by card straight after the booking (ADR 0082), instead of the salon asking for it later.</summary>
    public bool DepositPaidOnline { get; init; }

    /// <summary>The sentence that says the deposit before the client commits: how much, and how it will be paid.</summary>
    public string DepositTermsKey => (PriceIsFrom, DepositPaidOnline) switch
    {
        (false, false) => "Booking.Public.Deposit.Due",
        (true, false) => "Booking.Public.Deposit.DueFrom",
        (false, true) => "Booking.Public.Deposit.DueOnline",
        (true, true) => "Booking.Public.Deposit.DueFromOnline",
    };
}