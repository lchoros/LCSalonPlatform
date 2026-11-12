using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>
/// Rules for <see cref="AddCategoryCommand"/>: a name, at most <see cref="ServiceRules.CategoryNameMaxLength"/> characters
/// after trimming, not already used by the salon (case-insensitively; the unique index is the backstop). Messages come
/// from resource keys in the request culture.
/// </summary>
public sealed class AddCategoryValidator : AbstractValidator<AddCategoryCommand>
{
    public AddCategoryValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .Cascade(CascadeMode.Stop)
            .Must(ServiceRules.HasName)
            .WithMessage(_ => localizer["Catalog.Categories.Validation.NameRequired"])
            .Must(ServiceRules.CategoryNameFits)
            .WithMessage(_ => localizer.Format("Catalog.Categories.Validation.NameTooLong", ServiceRules.CategoryNameMaxLength))
            .MustAsync(async (name, cancellationToken) =>
                !await db.ServiceCategories.AnyAsync(c => EF.Functions.ILike(c.Name, ServiceRules.LikeLiteral(ServiceRules.NormalizeName(name))), cancellationToken))
            .WithMessage(_ => localizer["Catalog.Categories.Validation.NameTaken"]);
    }
}