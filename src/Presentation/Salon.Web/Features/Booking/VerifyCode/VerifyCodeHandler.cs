using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Booking.VerifyCode;

/// <summary>Runs inside the pipeline's transaction: the attempt count, the client, the appointment and its outbox event commit together.</summary>
public sealed class VerifyCodeHandler(IPublicBookingService publicBooking) : IRequestHandler<VerifyCodeCommand, PublicBookingVerification>
{
    public Task<PublicBookingVerification> HandleAsync(VerifyCodeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.VerifyAndBookAsync(request.VerificationId, request.Code, cancellationToken);
    }
}

public sealed class PendingVerificationHandler(IPublicBookingService publicBooking) : IRequestHandler<PendingVerificationQuery, PendingVerification?>
{
    public Task<PendingVerification?> HandleAsync(PendingVerificationQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return publicBooking.GetVerificationAsync(request.VerificationId, cancellationToken);
    }
}