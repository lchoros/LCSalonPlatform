using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.ManageCategories;

/// <summary>
/// Rules for <see cref="RenameCategoryCommand"/>: the same name rules as adding, except that the category may keep (or
/// re-case) its own name. Messages come from resource keys in the request culture.
/// </summary>
public sealed class RenameCategoryValidator : AbstractValidator<RenameCategoryCommand>
{
    public RenameCategoryValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .Cascade(CascadeMode.Stop)
            .Must(ServiceRules.HasName)
            .WithMessage(_ => localizer["Catalog.Categories.Validation.NameRequired"])
            .Must(ServiceRules.CategoryNameFits)
            .WithMessage(_ => localizer.Format("Catalog.Categories.Validation.NameTooLong", ServiceRules.CategoryNameMaxLength))
            .MustAsync(async (command, name, cancellationToken) =>
                !await db.ServiceCategories.AnyAsync(
                    c => c.Id != command.CategoryId && EF.Functions.ILike(c.Name, ServiceRules.LikeLiteral(ServiceRules.NormalizeName(name))),
                    cancellationToken))
            .WithMessage(_ => localizer["Catalog.Categories.Validation.NameTaken"]);
    }
}