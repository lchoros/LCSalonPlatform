using Salon.Core.Import;
using Salon.Plugin.Import.Csv;
using Salon.Plugin.Import.Parsers;

namespace Salon.Plugin.Import;

/// <summary>
/// Reads a price list from a CSV file (ADR 0066): refuses an oversized or binary file, decodes, splits with the row
/// cap and finds the columns by their headings, in English or Polish and in any order. Only a name column is needed;
/// a variant column (ADR 0073) splits one name into its variants, row by row.
/// Text is trimmed and numbers are read; whether a row can become or change a service is the classifier's to say.
/// The headings of the salon's own price list file (<c>admin/services/price-list.csv</c>) are among the aliases, so
/// that file can be edited in a spreadsheet and read back. Pure.
/// </summary>
public sealed class CsvServiceImportProvider : IServiceImportProvider
{
    private static readonly HashSet<string> NameAliases = HeaderKey.Set(
        "Name", "Service", "Service name", "Treatment", "Treatment name", "Nazwa", "Usługa", "Nazwa usługi", "Zabieg");

    private static readonly HashSet<string> VariantAliases = HeaderKey.Set(
        "Variant", "Option", "Wariant", "Opcja");

    private static readonly HashSet<string> CategoryAliases = HeaderKey.Set(
        "Category", "Section", "Heading", "Type", "Kategoria", "Dział");

    private static readonly HashSet<string> MinutesAliases = HeaderKey.Set(
        "Minutes", "Mins", "Min", "Duration", "Duration min", "Duration minutes", "Time", "Length", "Minuty", "Czas", "Czas trwania", "Czas min");

    private static readonly HashSet<string> PriceAliases = HeaderKey.Set(
        "Price", "Fee", "Amount", "Cena");

    private static readonly HashSet<string> DepositAliases = HeaderKey.Set(
        "Deposit", "Deposit percent", "Deposit percentage", "Zadatek", "Zadatek procent");

    public ServiceImportParseResult Parse(ReadOnlyMemory<byte> content)
    {
        if (content.Length > ClientImportLimits.MaxBytes)
        {
            return ServiceImportParseResult.Failed(ClientImportError.TooLarge);
        }

        if (!CsvDecoder.TryDecode(content.Span, out var text))
        {
            return ServiceImportParseResult.Failed(ClientImportError.NotText);
        }

        var document = CsvReader.Read(text, ServiceImportLimits.MaxRows);
        if (document.Header.Count == 0 || document.Records.Count == 0)
        {
            return ServiceImportParseResult.Failed(ClientImportError.Empty, document.Header);
        }

        if (document.Truncated)
        {
            return ServiceImportParseResult.Failed(ClientImportError.TooManyRows, document.Header);
        }

        var columns = Columns.Of(document.Header);
        if (columns.Name < 0)
        {
            return ServiceImportParseResult.Failed(ClientImportError.UnknownFormat, document.Header);
        }

        return new ServiceImportParseResult(document.Header, document.Records.Select(r => Row(r, columns, document.Header.Count)).ToList(), null);
    }

    private static ImportedServiceRow Row(ImportRecord record, Columns columns, int headerCount)
    {
        var problems = new List<ServiceImportProblem>();
        if (record.Fields.Count != headerCount)
        {
            problems.Add(ServiceImportProblem.WrongColumnCount);
        }

        // "45" and "45 min" are minutes; "1:30" and "1h 30" are not read, and the row says so.
        if (!ImportNumbers.TryWhole(Field(record, columns.Minutes), out var minutes))
        {
            problems.Add(ServiceImportProblem.InvalidMinutes);
        }

        if (!ImportNumbers.TryMoney(Field(record, columns.Price), out var price))
        {
            problems.Add(ServiceImportProblem.InvalidPrice);
        }

        // "20" and "20%" are a percentage: the sign is taken off before the number is read.
        if (!ImportNumbers.TryMoney(Field(record, columns.Deposit)?.Replace("%", string.Empty, StringComparison.Ordinal), out var deposit))
        {
            problems.Add(ServiceImportProblem.InvalidDeposit);
        }

        return new ImportedServiceRow(
            record.SourceRow,
            Field(record, columns.Name) ?? string.Empty,
            Field(record, columns.Variant),
            Field(record, columns.Category),
            minutes,
            price,
            deposit,
            problems);
    }

    /// <summary>The trimmed text of a column; null when the file has no such column, the row is short, or the cell is blank.</summary>
    private static string? Field(ImportRecord record, int index)
        => index >= 0 && index < record.Fields.Count && !string.IsNullOrWhiteSpace(record.Fields[index]) ? record.Fields[index].Trim() : null;

    /// <summary>Where each field is in the file; -1 for a column the file does not have. The first heading that fits wins.</summary>
    private sealed record Columns(int Name, int Variant, int Category, int Minutes, int Price, int Deposit)
    {
        public static Columns Of(IReadOnlyList<string> header)
        {
            var keys = header.Select(HeaderKey.Of).ToList();
            int Find(HashSet<string> aliases) => keys.FindIndex(aliases.Contains);
            return new Columns(Find(NameAliases), Find(VariantAliases), Find(CategoryAliases), Find(MinutesAliases), Find(PriceAliases), Find(DepositAliases));
        }
    }
}