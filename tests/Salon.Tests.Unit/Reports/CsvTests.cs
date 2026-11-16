using Salon.Core.Reports;

namespace Salon.Tests.Unit.Reports;

/// <summary>A table as CSV for a spreadsheet (ADR 0042): the reader's separator, quoting, and no formulas from typed text.</summary>
public class CsvTests
{
    [Fact]
    public void A_table_is_written_row_by_row_with_the_headers_first_and_cr_lf_line_ends()
    {
        var csv = Csv.Write(
            ["Date", "Client", "Amount"],
            [
                [CsvValue.Text("2026-11-16"), CsvValue.Text("Olivia Brown"), CsvValue.Number("45.00")],
                [CsvValue.Text("2026-11-16"), CsvValue.Text("Noah Reed"), CsvValue.Number("-12.50")],
            ],
            ',');

        csv.ShouldBe("Date,Client,Amount\r\n2026-11-16,Olivia Brown,45.00\r\n2026-11-16,Noah Reed,-12.50\r\n");
    }

    [Theory]
    [InlineData("Brown, Olivia", ',', "\"Brown, Olivia\"")]
    [InlineData("Brown, Olivia", ';', "Brown, Olivia")]
    [InlineData("Brown; Olivia", ';', "\"Brown; Olivia\"")]
    [InlineData("Olivia \"Liv\" Brown", ',', "\"Olivia \"\"Liv\"\" Brown\"")]
    [InlineData("line one\nline two", ',', "\"line one\nline two\"")]
    [InlineData("plain", ',', "plain")]
    [InlineData(null, ',', "")]
    public void A_cell_with_the_separator_a_quote_or_a_line_break_is_quoted(string? value, char separator, string expected)
    {
        Csv.Cell(CsvValue.Text(value), separator).ShouldBe(expected);
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.example\")", "\"'=HYPERLINK(\"\"http://evil.example\"\")\"")]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+447700900123", "'+447700900123")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tTabbed", "'\tTabbed")]
    public void Text_that_a_spreadsheet_would_run_as_a_formula_arrives_as_plain_text(string typed, string expected)
    {
        Csv.Cell(CsvValue.Text(typed), ',').ShouldBe(expected);
    }

    [Theory]
    [InlineData("-12.50", ',', "-12.50")]
    [InlineData("-12,50", ';', "-12,50")]
    [InlineData("-12,50", ',', "\"-12,50\"")]
    public void A_number_keeps_its_own_minus_and_is_quoted_only_when_it_holds_the_separator(string formatted, char separator, string expected)
    {
        Csv.Cell(CsvValue.Number(formatted), separator).ShouldBe(expected);
    }

    [Fact]
    public void A_table_without_rows_is_its_header_line()
    {
        Csv.Write(["Date", "Amount"], [], ';').ShouldBe("Date;Amount\r\n");
    }
}