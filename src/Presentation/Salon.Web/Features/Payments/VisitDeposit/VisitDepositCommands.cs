using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Deposit = Salon.Services.Payments.VisitDeposit;

namespace Salon.Web.Features.Payments.VisitDeposit;

/// <summary>
/// The deposit page of one visit (ADR 0023): what the booking asks for and what the salon received. Null when the salon
/// has no such appointment. A query: no transaction, tenant required.
/// </summary>
public sealed record VisitDepositQuery(Guid AppointmentId) : IQuery<Deposit?>;

/// <summary>Records that the salon received the visit's deposit; <paramref name="Amount"/> null takes what the booking asks for. Nothing is charged (Phase 1).</summary>
public sealed record RecordDepositCommand(Guid AppointmentId, decimal? Amount, PaymentMethod? Method, Guid? ByUserId) : ICommand<DepositLine>;

/// <summary>Records that the visit's deposit went back to the client, in full.</summary>
public sealed record ReturnDepositCommand(Guid AppointmentId, PaymentMethod? Method, Guid? ByUserId) : ICommand<DepositLine>;

public sealed class RecordDepositValidator : AbstractValidator<RecordDepositCommand>
{
    public RecordDepositValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        // The upper limit is the booked price, which only the account service knows; it refuses anything above.
        RuleFor(c => c.Amount)
            .Must(amount => amount is null || (amount > 0m && decimal.Round(amount.Value, 2) == amount))
            .WithMessage(_ => localizer["Payments.Deposit.Validation.Amount"]);

        RuleFor(c => c.Method)
            .Must(method => method is { } chosen && Enum.IsDefined(chosen))
            .WithMessage(_ => localizer["Payments.Deposit.Validation.MethodRequired"]);
    }
}

public sealed class ReturnDepositValidator : AbstractValidator<ReturnDepositCommand>
{
    public ReturnDepositValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Method)
            .Must(method => method is { } chosen && Enum.IsDefined(chosen))
            .WithMessage(_ => localizer["Payments.Deposit.Validation.MethodRequired"]);
    }
}

public sealed class VisitDepositHandler(IClientAccountService account) : IRequestHandler<VisitDepositQuery, Deposit?>
{
    public Task<Deposit?> HandleAsync(VisitDepositQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.GetDepositAsync(request.AppointmentId, cancellationToken);
    }
}

public sealed class RecordDepositHandler(IClientAccountService account) : IRequestHandler<RecordDepositCommand, DepositLine>
{
    public Task<DepositLine> HandleAsync(RecordDepositCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        var method = request.Method ?? throw new ArgumentException("A deposit needs its method.", nameof(request));
        return account.RecordDepositAsync(request.AppointmentId, request.Amount, method, request.ByUserId, cancellationToken);
    }
}

public sealed class ReturnDepositHandler(IClientAccountService account) : IRequestHandler<ReturnDepositCommand, DepositLine>
{
    public Task<DepositLine> HandleAsync(ReturnDepositCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        var method = request.Method ?? throw new ArgumentException("A return needs its method.", nameof(request));
        return account.ReturnDepositAsync(request.AppointmentId, method, request.ByUserId, cancellationToken);
    }
}