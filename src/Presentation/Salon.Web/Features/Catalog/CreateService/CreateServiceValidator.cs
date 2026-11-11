using FluentValidation;

using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>Input rules for <see cref="CreateServiceCommand"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class CreateServiceValidator : AbstractValidator<CreateServiceCommand>
{
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MinDurationMinutes = 5;
    public const int MaxDurationMinutes = 600;
    public const int DurationStepMinutes = 5;
    public const decimal MaxPrice = 100_000m;

    public CreateServiceValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.NameRequired"])
            .Must(name => name is null || name.Trim().Length <= NameMaxLength)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.NameTooLong", NameMaxLength));

        RuleFor(c => c.Description)
            .Must(description => description is null || description.Trim().Length <= DescriptionMaxLength)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.DescriptionTooLong", DescriptionMaxLength));

        RuleFor(c => c.DurationMinutes)
            .InclusiveBetween(MinDurationMinutes, MaxDurationMinutes)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.DurationOutOfRange", MinDurationMinutes, MaxDurationMinutes))
            .Must(minutes => minutes % DurationStepMinutes == 0)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.DurationStep", DurationStepMinutes));

        RuleFor(c => c.Price)
            .InclusiveBetween(0m, MaxPrice)
            .WithMessage(_ => localizer.Format("Catalog.CreateService.Validation.PriceOutOfRange", MaxPrice))
            .Must(price => decimal.Round(price, 2) == price)
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.PriceTooPrecise"]);
    }
}