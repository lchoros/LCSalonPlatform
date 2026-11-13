using FluentValidation;

using Salon.Core.Catalog;
using Salon.Core.Payments;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.EditService;

/// <summary>Input rules for <see cref="EditServiceCommand"/>, decided by <see cref="ServiceRules"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class EditServiceValidator : AbstractValidator<EditServiceCommand>
{
    public EditServiceValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .Must(ServiceRules.HasName)
            .WithMessage(_ => localizer["Catalog.Edit.Validation.NameRequired"])
            .Must(ServiceRules.NameFits)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.NameTooLong", ServiceRules.NameMaxLength));

        RuleFor(c => c.Description)
            .Must(ServiceRules.DescriptionFits)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.DescriptionTooLong", ServiceRules.DescriptionMaxLength));

        RuleFor(c => c.DurationMinutes)
            .Must(ServiceRules.IsDurationInRange)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.DurationOutOfRange", ServiceRules.MinDurationMinutes, ServiceRules.MaxDurationMinutes))
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.DurationStep", ServiceRules.StepMinutes));

        RuleFor(c => c.Price)
            .Must(ServiceRules.IsPriceInRange)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.PriceOutOfRange", ServiceRules.MaxPrice))
            .Must(ServiceRules.HasAtMostTwoDecimals)
            .WithMessage(_ => localizer["Catalog.Edit.Validation.PriceTooPrecise"]);

        RuleFor(c => c.DepositPercent)
            .Must(percent => percent is null or (>= 0 and <= (int)DepositRules.MaxPercent))
            .WithMessage(_ => localizer["Catalog.Edit.Validation.DepositPercentRange"]);

        RuleFor(c => c.BufferAfterMinutes)
            .Must(ServiceRules.IsBufferInRange)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.BufferOutOfRange", ServiceRules.MaxBufferAfterMinutes))
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.MinutesStep", ServiceRules.StepMinutes));

        RuleFor(c => c.ProcessingMinutes)
            .Must(ServiceRules.IsProcessingInRange)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.ProcessingOutOfRange", ServiceRules.MaxProcessingMinutes))
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.MinutesStep", ServiceRules.StepMinutes));

        RuleFor(c => c.ProcessingStartsAfterMinutes)
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.Edit.Validation.MinutesStep", ServiceRules.StepMinutes))
            .Must((c, startsAfter) => ServiceRules.ProcessingFitsInside(c.DurationMinutes, c.ProcessingMinutes, startsAfter))
            .WithMessage(_ => localizer["Catalog.Edit.Validation.ProcessingOutsideDuration"])
            .When(c => c.ProcessingMinutes > 0 && ServiceRules.IsProcessingInRange(c.ProcessingMinutes) && ServiceRules.IsDurationInRange(c.DurationMinutes));
    }
}