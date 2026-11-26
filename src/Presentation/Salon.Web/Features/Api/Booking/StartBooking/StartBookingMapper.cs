using NodaTime;
using Salon.Api.Contracts.Booking;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Booking.Details;

namespace Salon.Web.Features.Api.Booking.StartBooking;

/// <summary>
/// The posted visit as the details form's own requests (ADR 0097): the first service as the form's route, the rest as
/// its <c>Also</c> and <c>With</c>. "Anyone available" is no person, as on the form.
/// </summary>
public static class StartBookingMapper
{
    /// <summary>The check the details page makes before it shows the form: every service and person still bookable here.</summary>
    public static BookingSummaryQuery ToSummaryQuery(StartBookingRequest request, Instant start)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new BookingSummaryQuery(
            request.Parts[0].ServiceId,
            Person(request.Parts[0].Staff),
            start,
            [.. request.Parts.Skip(1).Select(p => p.ServiceId)],
            [.. request.Parts.Skip(1).Select(p => Person(p.Staff))]);
    }

    public static StartBookingCommand ToCommand(StartBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new StartBookingCommand(
            request.Parts[0].ServiceId,
            request.Start,
            Person(request.Parts[0].Staff),
            request.FirstName,
            request.LastName,
            request.Phone,
            request.Email,
            request.MarketingConsent,
            VisitBasket.Write(request.Parts.Skip(1).Select(p => p.ServiceId)),
            VisitBasket.WriteStaff(request.Parts.Skip(1).Select(p => p.Staff)));
    }

    private static Guid? Person(string? staff) => Guid.TryParse(staff, out var id) ? id : null;
}