using FluentValidation;

using Microsoft.EntityFrameworkCore;

using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Catalog.CreateService;

/// <summary>
/// The database has a unique index on (tenant, name); this turns a would-be constraint violation into a form message
/// and is stricter than the index: "Cut" and "cut" are the same service to a receptionist, so case is ignored.
/// Kept apart from <see cref="CreateServiceValidator"/> so the input rules stay testable without a database.
/// </summary>
public sealed class CreateServiceUniqueNameValidator : AbstractValidator<CreateServiceCommand>
{
    public CreateServiceUniqueNameValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .MustAsync(async (name, cancellationToken) =>
                string.IsNullOrWhiteSpace(name)
                || !await db.Services.AnyAsync(s => EF.Functions.ILike(s.Name, LikePattern(name.Trim())), cancellationToken))
            .WithMessage(_ => localizer["Catalog.CreateService.Validation.NameTaken"]);
    }

    /// <summary>Escapes the LIKE wildcards so the name is matched literally (Postgres' default escape character is the backslash).</summary>
    private static string LikePattern(string name)
        => name.Replace("\\", "\\\\", StringComparison.Ordinal)
               .Replace("%", "\\%", StringComparison.Ordinal)
               .Replace("_", "\\_", StringComparison.Ordinal);
}