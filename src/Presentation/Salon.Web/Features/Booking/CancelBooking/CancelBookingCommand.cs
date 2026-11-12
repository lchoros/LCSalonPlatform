using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Booking.CancelBooking;

/// <summary>The appointment behind a cancel link (ADR 0014); null for an unknown or malformed token. A query.</summary>
public sealed record CancelBookingQuery(string Token) : IQuery<PublicAppointment?>
{
    /// <summary>The token is a credential; it stays out of log lines.</summary>
    public override string ToString() => nameof(CancelBookingQuery);
}

/// <summary>Cancels by token as <see cref="Core.Appointments.AppointmentStatus.CancelledByClient"/>. Never throws for a client mistake: the outcome says what happened.</summary>
public sealed record CancelBookingCommand(string Token) : ICommand<PublicCancellation>
{
    public override string ToString() => nameof(CancelBookingCommand);
}

public sealed class CancelBookingQueryHandler(IPublicBookingService publicBooking) : IRequestHandler<CancelBookingQuery, PublicAppointment?>
{
    public Task<PublicAppointment?> HandleAsync(CancelBookingQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.FindByTokenAsync(request.Token, cancellationToken);
    }
}

public sealed class CancelBookingHandler(IPublicBookingService publicBooking) : IRequestHandler<CancelBookingCommand, PublicCancellation>
{
    public Task<PublicCancellation> HandleAsync(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.CancelByTokenAsync(request.Token, cancellationToken);
    }
}
