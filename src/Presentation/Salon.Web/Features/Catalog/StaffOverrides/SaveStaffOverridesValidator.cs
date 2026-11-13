using FluentValidation;

using Salon.Core.Catalog;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.StaffOverrides;

/// <summary>
/// Input rules for <see cref="SaveStaffOverridesCommand"/>, decided by <see cref="ServiceRules"/>: a blank is the default;
/// a duration obeys the service grid and bounds, a price the service range and precision. Messages come from resource keys,
/// resolved in the request culture, and sit on <c>Rows[i].DurationMinutes</c> / <c>Rows[i].Price</c> so they land next to their field.
/// </summary>
public sealed class SaveStaffOverridesValidator : AbstractValidator<SaveStaffOverridesCommand>
{
    public SaveStaffOverridesValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleForEach(c => c.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.DurationMinutes)
                .Must(minutes => minutes is null || ServiceRules.IsDurationInRange(minutes.Value))
                .WithMessage(_ => localizer.Format("Catalog.StaffOverrides.Validation.DurationOutOfRange", ServiceRules.MinDurationMinutes, ServiceRules.MaxDurationMinutes))
                .Must(minutes => minutes is null || ServiceRules.IsOnStep(minutes.Value))
                .WithMessage(_ => localizer.Format("Catalog.StaffOverrides.Validation.DurationStep", ServiceRules.StepMinutes));

            row.RuleFor(r => r.Price)
                .Must(price => price is null || ServiceRules.IsPriceInRange(price.Value))
                .WithMessage(_ => localizer.Format("Catalog.StaffOverrides.Validation.PriceOutOfRange", ServiceRules.MaxPrice))
                .Must(price => price is null || ServiceRules.HasAtMostTwoDecimals(price.Value))
                .WithMessage(_ => localizer["Catalog.StaffOverrides.Validation.PriceTooPrecise"]);
        });
    }
}