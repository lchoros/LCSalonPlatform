using System.Text;

namespace Salon.Core.Reports;

/// <summary>
/// One cell of a CSV row: text somebody typed, which must never run as a formula, or a number the application
/// formatted itself, whose leading minus is its own sign.
/// </summary>
public readonly record struct CsvValue(string? Value, bool IsNumber)
{
    public static CsvValue Text(string? value) => new(value, false);

    public static CsvValue Number(string formatted) => new(formatted, true);
}

/// <summary>
/// Writes a table as CSV for a spreadsheet (ADR 0042, RFC 4180). Pure. The separator is the reader's own (a comma in
/// Britain, a semicolon where the comma is the decimal sign), rows end with CR LF, a cell that holds the separator, a
/// quote or a line break is quoted, and text a spreadsheet would run as a formula is made plain first: names and notes
/// are typed by people, and "=HYPERLINK(...)" in a client's name must arrive as those characters.
/// </summary>
public static class Csv
{
    public const string ContentType = "text/csv; charset=utf-8";

    /// <summary>The characters a spreadsheet takes as the start of a formula.</summary>
    private const string FormulaStarts = "=+-@\t\r";

    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<CsvValue>> rows, char separator)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder();
        Row(text, headers.Select(CsvValue.Text).ToArray(), separator);
        foreach (var row in rows)
        {
            Row(text, row, separator);
        }

        return text.ToString();
    }

    /// <summary>
    /// One cell as it is written. Text that starts like a formula gets an apostrophe in front, which a spreadsheet
    /// shows as nothing and reads as "this is text"; a number is left as it is.
    /// </summary>
    public static string Cell(CsvValue value, char separator)
    {
        var cell = value.Value ?? string.Empty;
        if (!value.IsNumber && cell.Length > 0 && FormulaStarts.Contains(cell[0], StringComparison.Ordinal))
        {
            cell = "'" + cell;
        }

        var mustQuote = cell.Contains(separator, StringComparison.Ordinal)
            || cell.Contains('"', StringComparison.Ordinal)
            || cell.Contains('\n', StringComparison.Ordinal)
            || cell.Contains('\r', StringComparison.Ordinal);
        return mustQuote ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : cell;
    }

    private static void Row(StringBuilder text, IReadOnlyList<CsvValue> cells, char separator)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                text.Append(separator);
            }

            text.Append(Cell(cells[i], separator));
        }

        text.Append("\r\n");
    }
}