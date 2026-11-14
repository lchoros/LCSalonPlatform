using FluentValidation;
using Salon.Core.Cqrs;
using Salon.Core.Settings;
using Salon.Services.Localization;
using Salon.Services.Settings;

namespace Salon.Web.Features.Settings.CancellationPolicy;

/// <summary>The salon's cancellation policy as the form shows it (ADR 0022).</summary>
public sealed record GetCancellationPolicyQuery : IQuery<SaveCancellationPolicyCommand>;

/// <summary>
/// The policy form as posted: the salon's default deposit, the notice period in hours and both fees, all as whole
/// percentages of the booked price.
/// <see cref="SaveCancellationPolicyValidator"/> holds the rules.
/// </summary>
public sealed record SaveCancellationPolicyCommand(
    int? FreeChangeHours,
    int? LateCancellationFeePercent,
    int? NoShowFeePercent,
    int? DepositPercent = 0) : ICommand<bool>
{
    public static SaveCancellationPolicyCommand From(CancellationPolicySettings settings, BookingSettings booking)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(booking);
        return new SaveCancellationPolicyCommand(
            settings.FreeChangeHours,
            (int)decimal.Round(settings.LateCancellationFeePercent),
            (int)decimal.Round(settings.NoShowFeePercent),
            (int)decimal.Round(booking.DefaultDepositPercent));
    }
}

public sealed class SaveCancellationPolicyValidator : AbstractValidator<SaveCancellationPolicyCommand>
{
    public const int MaxPercent = 100;

    public SaveCancellationPolicyValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.FreeChangeHours)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.HoursRequired"])
            .InclusiveBetween(0, CancellationPolicySettings.MaxFreeChangeHours)
            .WithMessage(_ => localizer.Format("Settings.CancellationPolicy.Validation.HoursRange", CancellationPolicySettings.MaxFreeChangeHours));

        RuleFor(c => c.LateCancellationFeePercent)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.PercentRequired"])
            .InclusiveBetween(0, MaxPercent)
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.PercentRange"])
            // Without a notice period no cancellation is ever late, so the fee could never apply: say so instead of saving a dead rule.
            .Must((c, percent) => percent is not > 0 || c.FreeChangeHours is > 0)
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.LateFeeNeedsNotice"]);

        RuleFor(c => c.DepositPercent)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.PercentRequired"])
            .InclusiveBetween(0, MaxPercent)
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.PercentRange"]);

        RuleFor(c => c.NoShowFeePercent)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.PercentRequired"])
            .InclusiveBetween(0, MaxPercent)
            .WithMessage(_ => localizer["Settings.CancellationPolicy.Validation.PercentRange"]);
    }
}

public sealed class GetCancellationPolicyHandler(ISettingService settings) : IRequestHandler<GetCancellationPolicyQuery, SaveCancellationPolicyCommand>
{
    public async Task<SaveCancellationPolicyCommand> HandleAsync(GetCancellationPolicyQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SaveCancellationPolicyCommand.From(
            await settings.LoadAsync<CancellationPolicySettings>(cancellationToken),
            await settings.LoadAsync<BookingSettings>(cancellationToken));
    }
}

public sealed class SaveCancellationPolicyHandler(ISettingService settings) : IRequestHandler<SaveCancellationPolicyCommand, bool>
{
    public async Task<bool> HandleAsync(SaveCancellationPolicyCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; this only narrows the types.
        if (request.FreeChangeHours is not { } hours || request.LateCancellationFeePercent is not { } late || request.NoShowFeePercent is not { } noShow || request.DepositPercent is not { } deposit)
        {
            return false;
        }

        // Load first: a property this form does not show must keep its value.
        var current = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
        current.FreeChangeHours = hours;
        current.LateCancellationFeePercent = late;
        current.NoShowFeePercent = noShow;
        await settings.SaveAsync(current, cancellationToken);

        // The salon's default deposit lives with the other booking rules (ADR 0023); the rest of them keep their values.
        var booking = await settings.LoadAsync<BookingSettings>(cancellationToken);
        booking.DefaultDepositPercent = deposit;
        await settings.SaveAsync(booking, cancellationToken);
        return true;
    }
}