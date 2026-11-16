using Salon.Core.Import;
using Salon.Core.Products;
using Salon.Services.Clients.Import;

namespace Salon.Services.Products.Import;

/// <summary>
/// Pure classification of the rows of a product file against the salon's products (ADR 0045). Invalid wins over
/// duplicate; a name the salon has wins over a name repeated in the file; everything else is new and reserves its
/// name for the rows that follow. The rules are <see cref="ProductRules"/> and <see cref="StockRules"/>, the ones the
/// product form uses, so what the preview calls new will save.
/// </summary>
internal static class ProductImportClassifier
{
    /// <summary>A name as it is compared: trimmed and without case, because "Shampoo" and "shampoo" are one bottle to the desk.</summary>
    public static string NameKey(string? name) => ProductRules.NormalizeName(name).ToUpperInvariant();

    /// <param name="existingNames">The salon's product names as <see cref="NameKey"/>s.</param>
    public static IReadOnlyList<ClassifiedProductRow> Classify(IReadOnlyList<ImportedProductRow> rows, IReadOnlySet<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(existingNames);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ClassifiedProductRow>(rows.Count);
        foreach (var row in rows)
        {
            var problems = new List<ProductImportProblem>(row.Problems);
            Check(!ProductRules.HasName(row.Name), ProductImportProblem.MissingName);
            Check(!ProductRules.NameFits(row.Name), ProductImportProblem.NameTooLong);
            Check(!ProductRules.BrandFits(row.Brand), ProductImportProblem.BrandTooLong);
            Check(!ProductRules.CodeFits(row.Code), ProductImportProblem.CodeTooLong);
            Check(!ProductRules.IsPriceValid(row.Price), ProductImportProblem.InvalidPrice);
            Check(row.Stock is { } stock && !StockRules.IsCountable(stock), ProductImportProblem.InvalidStock);
            Check(!ProductRules.IsLowStockAtValid(row.LowStockAt), ProductImportProblem.InvalidLowStockAt);
            if (problems.Count > 0)
            {
                result.Add(new ClassifiedProductRow(row, ImportRowStatus.Invalid, problems));
                continue;
            }

            var key = NameKey(row.Name);
            if (existingNames.Contains(key))
            {
                result.Add(new ClassifiedProductRow(row, ImportRowStatus.Duplicate, [ProductImportProblem.DuplicateName]));
            }
            else if (!seen.Add(key))
            {
                result.Add(new ClassifiedProductRow(row, ImportRowStatus.Duplicate, [ProductImportProblem.DuplicateInFile]));
            }
            else
            {
                result.Add(new ClassifiedProductRow(row, ImportRowStatus.New, []));
            }

            void Check(bool wrong, ProductImportProblem problem)
            {
                if (wrong && !problems.Contains(problem))
                {
                    problems.Add(problem);
                }
            }
        }

        return result;
    }
}