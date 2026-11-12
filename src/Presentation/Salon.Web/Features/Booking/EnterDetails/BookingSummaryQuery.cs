using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// What the details page shows above the form (ADR 0014): the chosen service, person ("anyone" when null) and start, in
/// the salon's zone. Null when the service is unknown or inactive, or the person is not bookable here. A query.
/// </summary>
public sealed record BookingSummaryQuery(Guid ServiceId, Guid? StaffMemberId, Instant Start) : IQuery<BookingSummary?>;

/// <param name="StaffName">Null for "anyone available".</param>
/// <param name="RequireSmsVerification">Decides the wording of the submit button and the phone hint.</param>
public sealed record BookingSummary(
    string ServiceName,
    Duration Duration,
    Money Price,
    string? StaffName,
    Instant Start,
    DateTimeZone Zone,
    string SalonName,
    string ConsentWording,
    bool RequireSmsVerification);
