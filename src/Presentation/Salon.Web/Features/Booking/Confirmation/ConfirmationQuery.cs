using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Booking.Confirmation;

/// <summary>The confirmation page of an online booking (ADR 0014); null for an unknown id or an admin booking. A query.</summary>
public sealed record ConfirmationQuery(Guid AppointmentId) : IQuery<PublicAppointment?>;

public sealed class ConfirmationHandler(IPublicBookingService publicBooking) : IRequestHandler<ConfirmationQuery, PublicAppointment?>
{
    public Task<PublicAppointment?> HandleAsync(ConfirmationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.FindAsync(request.AppointmentId, cancellationToken);
    }
}