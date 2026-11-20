using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Services.Booking;
using Salon.Services.Payments;

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

/// <summary>
/// The deposit of a visit as its own client may see it on the confirmation (ADR 0082): whether the visit has one on
/// the account, and when the salon still holds it, how much and how it was paid. A query.
/// </summary>
public sealed record DepositReceivedQuery(Guid AppointmentId) : IQuery<DepositReceived?>;

/// <param name="IsHeld">False for a deposit that went back to the client.</param>
public sealed record DepositReceived(Money Amount, PaymentMethod? Method, string? MethodName, bool IsHeld);

public sealed class DepositReceivedHandler(IClientAccountService account) : IRequestHandler<DepositReceivedQuery, DepositReceived?>
{
    public async Task<DepositReceived?> HandleAsync(DepositReceivedQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var visit = await account.GetDepositAsync(request.AppointmentId, cancellationToken);
        return visit?.Deposit is { } line ? new DepositReceived(line.Amount, line.Method, line.MethodName, line.IsHeld) : null;
    }
}