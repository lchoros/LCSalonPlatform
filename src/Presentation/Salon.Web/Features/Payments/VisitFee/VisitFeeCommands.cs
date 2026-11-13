using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;

namespace Salon.Web.Features.Payments.VisitFee;

/// <summary>
/// The fee page of one visit (ADR 0022): what the cancellation policy allows, the fee on the client's account and how it
/// was settled. Null when the salon has no such appointment. A query: no transaction, tenant required.
/// </summary>
public sealed record VisitFeeQuery(Guid AppointmentId) : IQuery<AppointmentFee?>;

/// <summary>Adds the visit's fee to the client's account; <paramref name="Amount"/> null takes what the policy proposes.</summary>
public sealed record ChargeFeeCommand(Guid AppointmentId, decimal? Amount, Guid? ByUserId) : ICommand<FeeLine>;

/// <summary>Lets the visit's fee go, with the reason.</summary>
public sealed record WaiveFeeCommand(Guid AppointmentId, string? Reason, Guid? ByUserId) : ICommand<FeeLine>;

/// <summary>Records that the client paid the visit's fee at the salon. Nothing is charged (Phase 1).</summary>
public sealed record RecordFeePaymentCommand(Guid AppointmentId, PaymentMethod? Method, Guid? ByUserId) : ICommand<FeeLine>;

public sealed class ChargeFeeValidator : AbstractValidator<ChargeFeeCommand>
{
    public ChargeFeeValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        // The upper limit is the booked price, which only the account service knows; it refuses anything above.
        RuleFor(c => c.Amount)
            .Must(amount => amount is null || (amount > 0m && decimal.Round(amount.Value, 2) == amount))
            .WithMessage(_ => localizer["Payments.Fee.Validation.Amount"]);
    }
}

public sealed class WaiveFeeValidator : AbstractValidator<WaiveFeeCommand>
{
    public WaiveFeeValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Reason)
            .Cascade(CascadeMode.Stop)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage(_ => localizer["Payments.Fee.Validation.ReasonRequired"])
            .MaximumLength(ClientAccountEntry.NoteMaxLength)
            .WithMessage(_ => localizer.Format("Payments.Fee.Validation.ReasonTooLong", ClientAccountEntry.NoteMaxLength));
    }
}

public sealed class RecordFeePaymentValidator : AbstractValidator<RecordFeePaymentCommand>
{
    public RecordFeePaymentValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Method)
            .Must(method => method is { } chosen && Enum.IsDefined(chosen))
            .WithMessage(_ => localizer["Payments.Fee.Validation.MethodRequired"]);
    }
}

public sealed class VisitFeeHandler(IClientAccountService account) : IRequestHandler<VisitFeeQuery, AppointmentFee?>
{
    public Task<AppointmentFee?> HandleAsync(VisitFeeQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.GetFeeAsync(request.AppointmentId, cancellationToken);
    }
}

public sealed class ChargeFeeHandler(IClientAccountService account) : IRequestHandler<ChargeFeeCommand, FeeLine>
{
    public Task<FeeLine> HandleAsync(ChargeFeeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.ChargeFeeAsync(request.AppointmentId, request.Amount, request.ByUserId, cancellationToken);
    }
}

public sealed class WaiveFeeHandler(IClientAccountService account) : IRequestHandler<WaiveFeeCommand, FeeLine>
{
    public Task<FeeLine> HandleAsync(WaiveFeeCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.WaiveFeeAsync(request.AppointmentId, request.Reason, request.ByUserId, cancellationToken);
    }
}

public sealed class RecordFeePaymentHandler(IClientAccountService account) : IRequestHandler<RecordFeePaymentCommand, FeeLine>
{
    public Task<FeeLine> HandleAsync(RecordFeePaymentCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        var method = request.Method ?? throw new ArgumentException("A payment needs its method.", nameof(request));
        return account.RecordFeePaymentAsync(request.AppointmentId, method, request.ByUserId, cancellationToken);
    }
}