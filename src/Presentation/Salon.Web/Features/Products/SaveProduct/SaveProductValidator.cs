using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Products;
using Salon.Data;
using Salon.Services.Localization;

namespace Salon.Web.Features.Products.SaveProduct;

/// <summary>Input rules for <see cref="SaveProductCommand"/>, decided by <see cref="ProductRules"/> and <see cref="StockRules"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class SaveProductValidator : AbstractValidator<SaveProductCommand>
{
    public SaveProductValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .Must(ProductRules.HasName)
            .WithMessage(_ => localizer["Products.Save.Validation.NameRequired"])
            .Must(ProductRules.NameFits)
            .WithMessage(_ => localizer.Format("Products.Save.Validation.TooLong", ProductRules.NameMaxLength));

        RuleFor(c => c.Brand)
            .Must(ProductRules.BrandFits)
            .WithMessage(_ => localizer.Format("Products.Save.Validation.TooLong", ProductRules.BrandMaxLength));

        RuleFor(c => c.Code)
            .Must(ProductRules.CodeFits)
            .WithMessage(_ => localizer.Format("Products.Save.Validation.TooLong", ProductRules.CodeMaxLength));

        RuleFor(c => c.RetailPrice)
            .Must(ProductRules.IsPriceValid)
            .WithMessage(_ => localizer.Format("Products.Save.Validation.Price", ProductRules.MaxPrice));

        RuleFor(c => c.LowStockAt)
            .Must(ProductRules.IsLowStockAtValid)
            .WithMessage(_ => localizer.Format("Products.Save.Validation.LowStockAt", ProductRules.MaxLowStockAt));

        RuleFor(c => c.OpeningStock)
            .Must(opening => opening is null || StockRules.IsCountable(opening.Value))
            .WithMessage(_ => localizer.Format("Products.Save.Validation.OpeningStock", StockRules.MaxOnHand));
    }
}

/// <summary>
/// The rule that needs the database, kept apart so the input rules stay testable without one. The unique index on
/// (tenant, name) would refuse a duplicate anyway; this turns it into a form message and ignores case, because
/// "Shampoo" and "shampoo" are the same bottle to the desk. Reads through the tenant filter, so another salon's
/// products never count; the product being changed does not clash with itself.
/// </summary>
public sealed class SaveProductDatabaseValidator : AbstractValidator<SaveProductCommand>
{
    public SaveProductDatabaseValidator(SalonDbContext db, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Name)
            .MustAsync(async (command, name, cancellationToken) =>
                !ProductRules.HasName(name)
                || !await db.Products.AnyAsync(
                    p => p.Id != command.Id && EF.Functions.ILike(p.Name, ProductRules.LikeLiteral(ProductRules.NormalizeName(name))),
                    cancellationToken))
            .WithMessage(_ => localizer["Products.Save.Validation.NameTaken"]);
    }
}