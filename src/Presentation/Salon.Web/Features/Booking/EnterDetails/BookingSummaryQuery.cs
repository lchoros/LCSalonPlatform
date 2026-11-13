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
    Money? Deposit = null);