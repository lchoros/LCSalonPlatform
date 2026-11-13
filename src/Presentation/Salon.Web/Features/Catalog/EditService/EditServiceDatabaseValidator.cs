using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.EditService;

/// <summary>
/// The rules that need the database, kept apart from <see cref="EditServiceValidator"/> so the input rules stay testable
/// without one. The name must be unique per salon, case-insensitively ("Cut" and "cut" are the same service to a
/// receptionist), except against the service itself; a chosen category must belong to the salon. Both read through the
/// tenant filter, so another salon's rows never count.
/// </summary>
public sealed class EditServiceDatabaseValidator : AbstractValidator<EditServiceCommand>
{
    public EditServiceDatabaseValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .MustAsync(async (command, name, cancellationToken) =>
                !ServiceRules.HasName(name)
                || !await db.Services.AnyAsync(
                    s => s.Id != command.ServiceId && EF.Functions.ILike(s.Name, ServiceRules.LikeLiteral(ServiceRules.NormalizeName(name))),
                    cancellationToken))
            .WithMessage(_ => localizer["Catalog.Edit.Validation.NameTaken"]);

        RuleFor(c => c.CategoryId)
            .MustAsync(async (categoryId, cancellationToken) =>
                categoryId is null || await db.ServiceCategories.AnyAsync(c => c.Id == categoryId, cancellationToken))
            .WithMessage(_ => localizer["Catalog.Edit.Validation.CategoryUnknown"]);
    }
}