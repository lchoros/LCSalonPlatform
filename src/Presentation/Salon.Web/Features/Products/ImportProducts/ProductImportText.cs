using System.Globalization;
using Salon.Core.Domain;
using Salon.Core.Import;
using Salon.Core.Products;
using Salon.Services.Clients.Import;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Products.Import;

namespace Salon.Web.Features.Products.ImportProducts;

/// <summary>What the import preview says about a row (ADR 0045, ADR 0043): the words, kept out of the view.</summary>
public static class ProductImportText
{
    public static string Status(ILocalizationService localizer, ImportRowStatus status)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return status switch
        {
            ImportRowStatus.New => localizer["Products.Import.Status.New"],
            ImportRowStatus.Duplicate => localizer["Products.Import.Status.Duplicate"],
            _ => localizer["Products.Import.Status.Invalid"],
        };
    }

    public static string Problem(ILocalizationService localizer, ProductImportProblem problem)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        return problem switch
        {
            ProductImportProblem.WrongColumnCount => localizer["Products.Import.Problem.WrongColumnCount"],
            ProductImportProblem.MissingName => localizer["Products.Import.Problem.MissingName"],
            ProductImportProblem.NameTooLong => localizer.Format("Products.Import.Problem.NameTooLong", ProductRules.NameMaxLength),
            ProductImportProblem.BrandTooLong => localizer.Format("Products.Import.Problem.BrandTooLong", ProductRules.BrandMaxLength),
            ProductImportProblem.CodeTooLong => localizer.Format("Products.Import.Problem.CodeTooLong", ProductRules.CodeMaxLength),
            ProductImportProblem.InvalidPrice => localizer["Products.Import.Problem.InvalidPrice"],
            ProductImportProblem.InvalidStock => localizer["Products.Import.Problem.InvalidStock"],
            ProductImportProblem.InvalidLowStockAt => localizer["Products.Import.Problem.InvalidLowStockAt"],
            ProductImportProblem.DuplicateName => localizer["Products.Import.Problem.DuplicateName"],
            ProductImportProblem.DuplicateInFile => localizer["Products.Import.Problem.DuplicateInFile"],
            _ => problem.ToString(),
        };
    }

    /// <summary>
    /// One line under the name: brand, code, price and units as they will be saved, the parts the file does not give
    /// left out; a dash when it gives none of them.
    /// </summary>
    public static string Details(ILocalizationService localizer, ClassifiedProductRow row, string currency, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(culture);
        var product = row.Row;
        var parts = new[]
        {
            product.Brand,
            product.Code,
            product.Price is { } price ? new Money(price, currency).ToDisplayString(culture) : null,
            product.Stock is { } stock ? localizer.Format("Products.Import.Preview.OnShelf", stock) : null,
        };
        return string.Join(" · ", parts.Where(p => !string.IsNullOrEmpty(p))).OrDash();
    }
}