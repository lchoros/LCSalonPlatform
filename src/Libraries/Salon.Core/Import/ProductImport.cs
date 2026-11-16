using System.Globalization;

namespace Salon.Core.Import;

/// <summary>Why a product row cannot be imported as it is, or why it would only repeat a product the salon has (ADR 0045).</summary>
public enum ProductImportProblem
{
    /// <summary>The record has a different number of fields than the header.</summary>
    WrongColumnCount = 1,
    MissingName = 2,
    NameTooLong = 3,
    BrandTooLong = 4,
    CodeTooLong = 5,
    /// <summary>Not a price, below zero, more than two decimals or over the cap.</summary>
    InvalidPrice = 6,
    /// <summary>Not a whole number of units a shelf can hold.</summary>
    InvalidStock = 7,
    InvalidLowStockAt = 8,
    /// <summary>The salon already has a product with this name.</summary>
    DuplicateName = 9,
    /// <summary>An earlier row of the same file has the same name.</summary>
    DuplicateInFile = 10,
}

/// <summary>
/// One product as it appears in an import file (ADR 0045): text trimmed, numbers read. A number that could not be
/// read is null and named in <see cref="Problems"/>; the classifier adds what needs the salon's context.
/// </summary>
/// <param name="SourceRow">1-based line number of the record in the file, header included.</param>
/// <param name="Price">What a client pays for one; null when the file gives none.</param>
/// <param name="Stock">Units on the shelf today; null when the file gives none.</param>
public sealed record ImportedProductRow(
    int SourceRow,
    string Name,
    string? Brand,
    string? Code,
    decimal? Price,
    int? Stock,
    int? LowStockAt,
    IReadOnlyList<ProductImportProblem> Problems);

/// <summary>What the provider made of a file: rows, or an <see cref="Error"/> and no rows. The reasons a whole file is refused are those of a client file.</summary>
public sealed record ProductImportParseResult(IReadOnlyList<string> Header, IReadOnlyList<ImportedProductRow> Rows, ClientImportError? Error)
{
    public bool IsSuccess => Error is null;

    public static ProductImportParseResult Failed(ClientImportError error, IReadOnlyList<string>? header = null) => new(header ?? [], [], error);
}

/// <summary>
/// Turns the bytes of an uploaded file into <see cref="ImportedProductRow"/>s (ADR 0045): decodes, splits and finds
/// the columns by their headings. Pure: nothing here touches the database or the tenant.
/// </summary>
public interface IProductImportProvider
{
    ProductImportParseResult Parse(ReadOnlyMemory<byte> content);
}

/// <summary>Caps on one product file (ADR 0045); the byte cap is <see cref="ClientImportLimits.MaxBytes"/>.</summary>
public static class ProductImportLimits
{
    /// <summary>More than any salon stocks; keeps one confirm inside one short transaction.</summary>
    public const int MaxRows = 2_000;
}

/// <summary>
/// Reads the numbers of an import file as a person typed them into a spreadsheet (ADR 0045): "12.50", "12,50",
/// "£1,250.00", "1 250,00 zł". Pure.
/// </summary>
public static class ImportNumbers
{
    /// <summary>
    /// True with null for a blank cell, true with the amount for a number, false for anything else. With both a comma
    /// and a full stop the last one is the decimal sign. With one of them, it is the decimal sign when one or two
    /// digits follow it once, and a thousands sign otherwise: no price has three decimals.
    /// </summary>
    public static bool TryMoney(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // A currency sign or code before or after the number is how spreadsheets export a price; nothing else may
        // stand there, and inside the number only digits, the two signs and spacing between thousands.
        var first = text.AsSpan().IndexOfAnyInRange('0', '9');
        var last = text.AsSpan().LastIndexOfAnyInRange('0', '9');
        if (first < 0 || !text[..first].All(IsAround) || !text[(last + 1)..].All(IsAround))
        {
            return false;
        }

        var digits = new string(text[first..(last + 1)].Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!digits.All(c => char.IsAsciiDigit(c) || c is '.' or ','))
        {
            return false;
        }

        var lastComma = digits.LastIndexOf(',');
        var lastStop = digits.LastIndexOf('.');
        var decimalAt = -1;
        if (lastComma >= 0 && lastStop >= 0)
        {
            decimalAt = Math.Max(lastComma, lastStop);
        }
        else if (Math.Max(lastComma, lastStop) is var at and >= 0)
        {
            var once = digits.IndexOf(digits[at], StringComparison.Ordinal) == at;
            decimalAt = once && digits.Length - at - 1 is 1 or 2 ? at : -1;
        }

        var whole = decimalAt >= 0 ? digits[..decimalAt] : digits;
        var fraction = decimalAt >= 0 ? digits[(decimalAt + 1)..] : string.Empty;
        if (!TryUngroup(whole, out var units)
            || !decimal.TryParse(fraction.Length > 0 ? units + "." + fraction : units, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>What may stand before or after the number: a currency sign, the letters of a currency code, spacing.</summary>
    private static bool IsAround(char c)
        => char.IsWhiteSpace(c) || char.IsLetter(c) || char.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol;

    /// <summary>
    /// The digits before the decimal sign without their thousands signs. One kind of sign only, and after the first
    /// group every group has three digits: "1,250" and "1.250.000" are numbers, "12.50.3" is not.
    /// </summary>
    private static bool TryUngroup(string whole, out string units)
    {
        units = whole;
        var hasComma = whole.Contains(',', StringComparison.Ordinal);
        var hasStop = whole.Contains('.', StringComparison.Ordinal);
        if (!hasComma && !hasStop)
        {
            return whole.Length > 0;
        }

        if (hasComma && hasStop)
        {
            return false;
        }

        var groups = whole.Split(hasComma ? ',' : '.');
        if (groups[0].Length is 0 or > 3 || groups.Skip(1).Any(g => g.Length != 3))
        {
            return false;
        }

        units = string.Concat(groups);
        return true;
    }

    /// <summary>True with null for a blank cell, true with the number for a whole number ("12", "12.00", "1,250"), false otherwise.</summary>
    public static bool TryWhole(string? text, out int? value)
    {
        value = null;
        if (!TryMoney(text, out var amount))
        {
            return false;
        }

        if (amount is not { } number)
        {
            return true;
        }

        if (number != decimal.Truncate(number) || number > int.MaxValue)
        {
            return false;
        }

        value = (int)number;
        return true;
    }
}