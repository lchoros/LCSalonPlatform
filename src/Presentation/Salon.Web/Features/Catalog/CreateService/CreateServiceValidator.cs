using FluentValidation;

using Salon.Core.Catalog;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>Input rules for <see cref="CreateServiceCommand"/>, decided by <see cref="ServiceRules"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .Must(ServiceRules.HasName)
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.NameRequired"])
            .Must(ServiceRules.NameFits)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.NameTooLong", ServiceRules.NameMaxLength));

        RuleFor(c => c.Description)
            .Must(ServiceRules.DescriptionFits)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.DescriptionTooLong", ServiceRules.DescriptionMaxLength));

        RuleFor(c => c.DurationMinutes)
            .Must(ServiceRules.IsDurationInRange)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.DurationOutOfRange", ServiceRules.MinDurationMinutes, ServiceRules.MaxDurationMinutes))
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.DurationStep", ServiceRules.StepMinutes));

        RuleFor(c => c.Price)
            .Must(ServiceRules.IsPriceInRange)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.PriceOutOfRange", ServiceRules.MaxPrice))
            .Must(ServiceRules.HasAtMostTwoDecimals)
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.PriceTooPrecise"]);

        RuleFor(c => c.BufferAfterMinutes)
            .Must(ServiceRules.IsBufferInRange)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.BufferOutOfRange", ServiceRules.MaxBufferAfterMinutes))
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.MinutesStep", ServiceRules.StepMinutes));

        RuleFor(c => c.ProcessingMinutes)
            .Must(ServiceRules.IsProcessingInRange)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.ProcessingOutOfRange", ServiceRules.MaxProcessingMinutes))
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.MinutesStep", ServiceRules.StepMinutes));

        RuleFor(c => c.ProcessingStartsAfterMinutes)
            .Must(ServiceRules.IsOnStep)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.MinutesStep", ServiceRules.StepMinutes))
            .Must((c, startsAfter) => ServiceRules.ProcessingFitsInside(c.DurationMinutes, c.ProcessingMinutes, startsAfter))
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.ProcessingOutsideDuration"])
            .When(c => c.ProcessingMinutes > 0 && ServiceRules.IsProcessingInRange(c.ProcessingMinutes) && ServiceRules.IsDurationInRange(c.DurationMinutes));
    }
}