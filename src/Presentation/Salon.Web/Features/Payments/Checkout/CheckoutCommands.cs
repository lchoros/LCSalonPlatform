using FluentValidation;

using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Services.Payments;

namespace Salon.Web.Features.Payments.Checkout;

/// <summary>
/// The bill of one visit (ADR 0024): price, deposit held, what is left, the client's open fees. Null when the salon has
/// no such appointment. A query: no transaction, tenant required.
/// </summary>
public sealed record CheckoutQuery(Guid AppointmentId) : IQuery<VisitBill?>;

/// <summary>
/// Records that the client paid the bill with one method: what is left of the visit and the open fees ticked on the
/// form (<paramref name="Fees"/>: the visits those fees are about). An arrived visit is completed in the same
/// transaction. Nothing is charged (Phase 1).
/// </summary>
public sealed record PayBillCommand(Guid AppointmentId, PaymentMethod? Method, IReadOnlyList<Guid> Fees, Guid? ByUserId) : ICommand<VisitBill>;

public sealed class PayBillValidator : AbstractValidator<PayBillCommand>
{
    public PayBillValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Method)
            .Must(method => method is { } chosen && Enum.IsDefined(chosen))
            .WithMessage(_ => localizer["Payments.Checkout.Validation.MethodRequired"]);
    }
}

public sealed class CheckoutHandler(IClientAccountService account) : IRequestHandler<CheckoutQuery, VisitBill?>
{
    public Task<VisitBill?> HandleAsync(CheckoutQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.GetBillAsync(request.AppointmentId, cancellationToken);
    }
}

/// <summary>
/// Runs inside the pipeline's transaction (ADR 0003): the payment lines and the visit's move to "completed" commit
/// together or not at all.
/// </summary>
public sealed class PayBillHandler(IClientAccountService account, IBookingService booking) : IRequestHandler<PayBillCommand, VisitBill>
{
    public async Task<VisitBill> HandleAsync(PayBillCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        var method = request.Method ?? throw new ArgumentException("A payment needs its method.", nameof(request));
        var bill = await account.PayBillAsync(request.AppointmentId, method, request.Fees, request.ByUserId, cancellationToken);
        if (bill.Status != AppointmentStatus.Arrived)
        {
            return bill;
        }

        // Paying is how a visit ends at the desk: no second tap on "Complete".
        await booking.CompleteAsync(request.AppointmentId, request.ByUserId, cancellationToken);
        return bill with { Status = AppointmentStatus.Completed };
    }
}