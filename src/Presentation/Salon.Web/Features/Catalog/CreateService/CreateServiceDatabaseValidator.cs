using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// The rules that need the database, kept apart from <see cref="CreateServiceValidator"/> so the input rules stay testable
/// without one. The unique index on (tenant, name) would refuse a duplicate anyway; this turns it into a form message and
/// is stricter than the index: "Cut" and "cut" are the same service to a receptionist, so case is ignored. A chosen
/// category must belong to the salon. Both read through the tenant filter, so another salon's rows never count.
/// </summary>
public sealed class CreateServiceDatabaseValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceDatabaseValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .MustAsync(async (name, cancellationToken) =>
                !ServiceRules.HasName(name)
                || !await db.Services.AnyAsync(s => EF.Functions.ILike(s.Name, ServiceRules.LikeLiteral(ServiceRules.NormalizeName(name))), cancellationToken))
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.NameTaken"]);

        RuleFor(c => c.CategoryId)
            .MustAsync(async (categoryId, cancellationToken) =>
                categoryId is null || await db.ServiceCategories.AnyAsync(c => c.Id == categoryId, cancellationToken))
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.CategoryUnknown"]);
    }
}