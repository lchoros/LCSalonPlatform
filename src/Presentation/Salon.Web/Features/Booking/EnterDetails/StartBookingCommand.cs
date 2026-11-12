using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// Step 3 posted (ADR 0014): the chosen slot plus the client's details. Sends the SMS code (returning the verification to
/// complete) or, when the salon has switched verification off, books at once. Bound straight from the form;
/// <see cref="StartBookingValidator"/> holds the field rules and <see cref="StartBookingPhoneValidator"/> the tenant-aware phone rule.
/// </summary>
/// <param name="ServiceId">From the route.</param>
/// <param name="Start">The slot as an ISO-8601 instant (<c>2026-11-18T10:00:00Z</c>), as the time picker linked it.</param>
/// <param name="StaffMemberId">Null for "anyone available".</param>
/// <param name="MarketingConsent">The tick; the wording recorded is the label shown next to it.</param>
public sealed record StartBookingCommand(
    Guid ServiceId,
    string? Start,
    Guid? StaffMemberId,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    bool MarketingConsent) : ICommand<PublicBookingStart>
{
    /// <summary>Keeps the phone and email out of any log line that prints the request.</summary>
    public override string ToString() => $"{nameof(StartBookingCommand)} {{ ServiceId = {ServiceId}, Start = {Start} }}";
}