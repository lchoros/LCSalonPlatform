using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Api.Booking.ShowVisit;

/// <summary>
/// GET visits/{id} of the booking API (ADR 0097): what <c>/book/done/{id}</c> shows. Null for an id that is not an online
/// booking of this salon. A query.
/// </summary>
public sealed record BookedVisitQuery(Guid AppointmentId) : IQuery<PublicAppointment?>;

public sealed class BookedVisitHandler(IPublicBookingService publicBooking) : IRequestHandler<BookedVisitQuery, PublicAppointment?>
{
    public Task<PublicAppointment?> HandleAsync(BookedVisitQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.FindAsync(request.AppointmentId, cancellationToken);
    }
}