using Salon.Core.Import;
using Salon.Plugin.Import.Csv;
using Salon.Plugin.Import.Parsers;

namespace Salon.Plugin.Import;

/// <summary>
/// Reads a product list from a CSV file (ADR 0045): refuses an oversized or binary file, decodes, splits with the row
/// cap and finds the columns by their headings, in English or Polish and in any order. Only a name column is needed.
/// Text is trimmed and numbers are read; whether a row can become a product is the classifier's to say. Pure.
/// </summary>
public sealed class CsvProductImportProvider : IProductImportProvider
{
    private static readonly HashSet<string> NameAliases = HeaderKey.Set(
        "Name", "Product", "Product name", "Item", "Item name", "Description", "Nazwa", "Produkt", "Nazwa produktu");

    private static readonly HashSet<string> BrandAliases = HeaderKey.Set(
        "Brand", "Make", "Manufacturer", "Range", "Supplier", "Marka", "Producent");

    private static readonly HashSet<string> CodeAliases = HeaderKey.Set(
        "Code", "Product code", "Barcode", "SKU", "EAN", "Kod", "Kod produktu", "Kod kreskowy");

    private static readonly HashSet<string> PriceAliases = HeaderKey.Set(
        "Price", "Retail price", "Selling price", "Sale price", "RRP", "Cena", "Cena sprzedaży", "Cena detaliczna");

    private static readonly HashSet<string> StockAliases = HeaderKey.Set(
        "Stock", "Quantity", "Qty", "On hand", "In stock", "Stock on hand", "Stan", "Ilość", "Stan magazynowy");

    private static readonly HashSet<string> LowStockAliases = HeaderKey.Set(
        "Low stock at", "Low stock", "Reorder at", "Reorder level", "Minimum", "Min", "Min stock", "Stan minimalny");

    public ProductImportParseResult Parse(ReadOnlyMemory<byte> content)
    {
        if (content.Length > ClientImportLimits.MaxBytes)
        {
            return ProductImportParseResult.Failed(ClientImportError.TooLarge);
        }

        if (!CsvDecoder.TryDecode(content.Span, out var text))
        {
            return ProductImportParseResult.Failed(ClientImportError.NotText);
        }

        var document = CsvReader.Read(text, ProductImportLimits.MaxRows);
        if (document.Header.Count == 0 || document.Records.Count == 0)
        {
            return ProductImportParseResult.Failed(ClientImportError.Empty, document.Header);
        }

        if (document.Truncated)
        {
            return ProductImportParseResult.Failed(ClientImportError.TooManyRows, document.Header);
        }

        var columns = Columns.Of(document.Header);
        if (columns.Name < 0)
        {
            return ProductImportParseResult.Failed(ClientImportError.UnknownFormat, document.Header);
        }

        return new ProductImportParseResult(document.Header, document.Records.Select(r => Row(r, columns, document.Header.Count)).ToList(), null);
    }

    private static ImportedProductRow Row(ImportRecord record, Columns columns, int headerCount)
    {
        var problems = new List<ProductImportProblem>();
        if (record.Fields.Count != headerCount)
        {
            problems.Add(ProductImportProblem.WrongColumnCount);
        }

        if (!ImportNumbers.TryMoney(Field(record, columns.Price), out var price))
        {
            problems.Add(ProductImportProblem.InvalidPrice);
        }

        if (!ImportNumbers.TryWhole(Field(record, columns.Stock), out var stock))
        {
            problems.Add(ProductImportProblem.InvalidStock);
        }

        if (!ImportNumbers.TryWhole(Field(record, columns.LowStockAt), out var lowStockAt))
        {
            problems.Add(ProductImportProblem.InvalidLowStockAt);
        }

        return new ImportedProductRow(
            record.SourceRow,
            Field(record, columns.Name) ?? string.Empty,
            Field(record, columns.Brand),
            Field(record, columns.Code),
            price,
            stock,
            lowStockAt,
            problems);
    }

    /// <summary>The trimmed text of a column; null when the file has no such column, the row is short, or the cell is blank.</summary>
    private static string? Field(ImportRecord record, int index)
        => index >= 0 && index < record.Fields.Count && !string.IsNullOrWhiteSpace(record.Fields[index]) ? record.Fields[index].Trim() : null;

    /// <summary>Where each field is in the file; -1 for a column the file does not have. The first heading that fits wins.</summary>
    private sealed record Columns(int Name, int Brand, int Code, int Price, int Stock, int LowStockAt)
    {
        public static Columns Of(IReadOnlyList<string> header)
        {
            var keys = header.Select(HeaderKey.Of).ToList();
            int Find(HashSet<string> aliases) => keys.FindIndex(aliases.Contains);
            return new Columns(Find(NameAliases), Find(BrandAliases), Find(CodeAliases), Find(PriceAliases), Find(StockAliases), Find(LowStockAliases));
        }
    }
}