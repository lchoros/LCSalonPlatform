using System.Text;

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Salon.Core.Import;
using Salon.Plugin.Import.Csv;
using Salon.Plugin.Import.Parsers;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0019: whatever a CSV writer that follows RFC 4180 emits (quotes, separators, line breaks inside fields, any
/// separator) the reader gives back unchanged; and generated client rows survive the Booksy header and parser.
/// </summary>
public class CsvRoundTripPropertyTests
{
    private static readonly char[] Separators = [',', ';', '\t'];

    [Property(MaxTest = 200)]
    public Property Any_table_written_as_rfc_4180_reads_back_field_for_field()
    {
        var fieldGen = ArbMap.Default.GeneratorFor<string>().Select(s => s ?? string.Empty);
        var tableGen =
            from width in Gen.Choose(1, 6)
            from separator in Gen.Elements(Separators)
            from rows in fieldGen.ArrayOf(width).ArrayOf()
            select (width, separator, rows);

        return Prop.ForAll(tableGen.ToArbitrary(), table =>
        {
            var (width, separator, rows) = table;
            var header = Enumerable.Range(1, width).Select(i => "col" + i).ToArray();
            var text = Write(separator, header, rows);

            var document = CsvReader.Read(text);

            document.Separator.ShouldBe(separator);
            document.Header.ShouldBe(header);
            document.Records.Count.ShouldBe(rows.Length);
            for (var i = 0; i < rows.Length; i++)
            {
                document.Records[i].Fields.ShouldBe(rows[i], $"record {i}");
            }
        });
    }

    [Property(MaxTest = 100)]
    public Property Generated_clients_survive_the_booksy_header_and_parser()
    {
        var letters = Gen.Elements("abcdefghijklmnopqrstuvwxyząćęłńóśźżABCDEFGHIJKLMNOPQRSTUVWXYZĄĆĘŁŃÓŚŹŻ".ToCharArray());
        var word = from length in Gen.Choose(1, 12) from chars in letters.ArrayOf(length) select new string(chars);
        var phone = from digits in Gen.Choose(0, 9).ArrayOf(9) select "+44" + string.Concat(digits);
        var email = from local in word from domain in word select $"{local}@{domain}.com";
        var notes = Gen.OneOf(Gen.Constant<string?>(null), ArbMap.Default.GeneratorFor<string>().Select(s => string.IsNullOrWhiteSpace(s) ? null : s.Trim()));
        var row =
            from first in word
            from last in word
            from p in phone
            from e in Gen.OneOf(Gen.Constant<string?>(null), email.Select(x => (string?)x))
            from n in notes
            from consent in ArbMap.Default.GeneratorFor<bool>()
            select new ImportedClientRow(0, first, last, p, e, n, consent, []);

        return Prop.ForAll(row.ArrayOf().ToArbitrary(), rows =>
        {
            string[] header = ["First Name", "Last Name", "Phone Number", "Email", "Customer notes", "Accepts Marketing"];
            var cells = rows.Select(r => new[] { r.FirstName, r.LastName, r.Phone ?? string.Empty, r.Email ?? string.Empty, r.Notes ?? string.Empty, r.MarketingConsent ? "Yes" : "No" }).ToArray();
            var text = Write(',', header, cells);

            var document = CsvReader.Read(text);
            var parsed = new BooksyCsvParser().Parse(document.Header, document.Records);

            parsed.Count.ShouldBe(rows.Length);
            for (var i = 0; i < rows.Length; i++)
            {
                parsed[i].ShouldBe(rows[i] with { SourceRow = parsed[i].SourceRow }, $"row {i}");
                parsed[i].Problems.ShouldBeEmpty();
            }
        });
    }

    /// <summary>RFC 4180 writer: quotes a field that holds the separator, a quote, a line break or edge whitespace, and every empty field.</summary>
    private static string Write(char separator, string[] header, string[][] rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(separator, header)).Append("\r\n");
        foreach (var row in rows)
        {
            builder.Append(string.Join(separator, row.Select(f => Quote(f, separator)))).Append("\r\n");
        }

        return builder.ToString();
    }

    private static string Quote(string field, char separator)
    {
        var needsQuotes = field.Length == 0
            || field.Contains(separator, StringComparison.Ordinal)
            || field.Contains('"', StringComparison.Ordinal)
            || field.Contains('\r', StringComparison.Ordinal)
            || field.Contains('\n', StringComparison.Ordinal)
            || char.IsWhiteSpace(field[0])
            || char.IsWhiteSpace(field[^1]);
        return needsQuotes ? "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : field;
    }
}
