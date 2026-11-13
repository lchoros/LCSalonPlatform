using FluentValidation;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;

using Count = Salon.Core.Payments.CashUp;

namespace Salon.Web.Features.Payments.CashUp;

/// <summary>
/// The till of one salon day (ADR 0024): money in and out per method and the end-of-day count, if any.
/// <paramref name="Day"/> null is today in the salon's zone. A query: no transaction, tenant required.
/// </summary>
public sealed record CashUpQuery(LocalDate? Day) : IQuery<CashDay>;

/// <summary>Closes <paramref name="Day"/> with the cash the person counted and an optional remark about a difference.</summary>
public sealed record CloseCashUpCommand(LocalDate Day, decimal? CountedCash, string? Note, Guid? ByUserId) : ICommand<CashUpResult>;

public sealed class CloseCashUpValidator : AbstractValidator<CloseCashUpCommand>
{
    public CloseCashUpValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.CountedCash)
            .Must(counted => counted is { } amount && TillRules.IsCountable(amount))
            .WithMessage(_ => localizer["Payments.CashUp.Validation.Counted"]);

        RuleFor(c => c.Note)
            .MaximumLength(Count.NoteMaxLength)
            .WithMessage(_ => localizer.Format("Payments.CashUp.Validation.NoteTooLong", Count.NoteMaxLength));
    }
}

public sealed class CashUpHandler(ICashUpService cashUp) : IRequestHandler<CashUpQuery, CashDay>
{
    public Task<CashDay> HandleAsync(CashUpQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return cashUp.GetDayAsync(request.Day, cancellationToken);
    }
}

public sealed class CloseCashUpHandler(ICashUpService cashUp) : IRequestHandler<CloseCashUpCommand, CashUpResult>
{
    public Task<CashUpResult> HandleAsync(CloseCashUpCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the type.
        var counted = request.CountedCash ?? throw new ArgumentException("A cash-up needs the counted amount.", nameof(request));
        return cashUp.CloseAsync(request.Day, counted, request.Note, request.ByUserId, cancellationToken);
    }
}