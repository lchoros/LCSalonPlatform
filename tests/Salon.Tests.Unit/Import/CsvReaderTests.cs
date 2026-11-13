using Salon.Plugin.Import.Csv;

namespace Salon.Tests.Unit.Import;

/// <summary>ADR 0019: a small RFC 4180 reader that is tolerant where exports are sloppy (separator, trimming, blank lines).</summary>
public class CsvReaderTests
{
    [Fact]
    public void Reads_a_comma_separated_file_with_line_numbers()
    {
        var document = CsvReader.Read("a,b\n1,2\n3,4\n");

        document.Separator.ShouldBe(',');
        document.Header.ShouldBe(["a", "b"]);
        document.Records.Count.ShouldBe(2);
        document.Records[0].SourceRow.ShouldBe(2);
        document.Records[0].Fields.ShouldBe(["1", "2"]);
        document.Records[1].SourceRow.ShouldBe(3);
        document.Records[1].Fields.ShouldBe(["3", "4"]);
        document.Truncated.ShouldBeFalse();
    }

    [Theory]
    [InlineData("a;b\n1;2", ';')]
    [InlineData("a\tb\n1\t2", '\t')]
    [InlineData("Imię;Nazwisko;Opis, uwagi\nx;y;z", ';')]
    [InlineData("First Name,Last Name,Notes; more\nx,y,z", ',')]
    public void Detects_the_separator_from_the_header_line(string text, char separator)
    {
        var document = CsvReader.Read(text);

        document.Separator.ShouldBe(separator);
        document.Header.Count.ShouldBe(text.Split('\n')[0].Count(c => c == separator) + 1);
        document.Records.ShouldHaveSingleItem().Fields.Count.ShouldBe(document.Header.Count);
    }

    [Fact]
    public void Quoted_fields_keep_separators_and_unescape_doubled_quotes()
    {
        var document = CsvReader.Read("\"name\",\"note\"\n\"Brown, Olivia\",\"says \"\"hi\"\"\"\n");

        document.Header.ShouldBe(["name", "note"]);
        document.Records.ShouldHaveSingleItem().Fields.ShouldBe(["Brown, Olivia", "says \"hi\""]);
    }

    [Fact]
    public void A_quoted_field_may_span_lines_and_the_next_record_keeps_its_real_line_number()
    {
        var document = CsvReader.Read("a,b\r\n\"line1\r\nline2\",x\r\n3,4\r\n");

        document.Records.Count.ShouldBe(2);
        document.Records[0].SourceRow.ShouldBe(2);
        document.Records[0].Fields.ShouldBe(["line1\r\nline2", "x"]);
        document.Records[1].SourceRow.ShouldBe(4);
        document.Records[1].Fields.ShouldBe(["3", "4"]);
    }

    [Fact]
    public void Blank_lines_are_skipped_but_still_counted()
    {
        var document = CsvReader.Read("a,b\n\n1,2\n   \n\n3,4\n\n");

        document.Records.Count.ShouldBe(2);
        document.Records[0].SourceRow.ShouldBe(3);
        document.Records[1].SourceRow.ShouldBe(6);
    }

    [Fact]
    public void Unquoted_fields_are_trimmed_and_quoted_fields_are_kept_verbatim()
    {
        var document = CsvReader.Read(" a , b \n 1 ,\" 2 \"\n");

        document.Header.ShouldBe(["a", "b"]);
        document.Records.ShouldHaveSingleItem().Fields.ShouldBe(["1", " 2 "]);
    }

    [Fact]
    public void Stops_at_the_record_cap_and_says_so()
    {
        var document = CsvReader.Read("a\n1\n2\n3\n", maxRecords: 2);

        document.Records.Count.ShouldBe(2);
        document.Truncated.ShouldBeTrue();
        CsvReader.Read("a\n1\n2\n", maxRecords: 2).Truncated.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n")]
    public void Empty_text_has_no_header_and_no_records(string text)
    {
        var document = CsvReader.Read(text);

        document.Header.ShouldBeEmpty();
        document.Records.ShouldBeEmpty();
    }

    [Fact]
    public void A_header_only_file_has_no_records()
    {
        var document = CsvReader.Read("First Name,Last Name\n");

        document.Header.ShouldBe(["First Name", "Last Name"]);
        document.Records.ShouldBeEmpty();
        document.Truncated.ShouldBeFalse();
    }

    [Fact]
    public void Ragged_records_are_returned_as_they_are_for_the_parser_to_flag()
    {
        var document = CsvReader.Read("a,b,c\n1,2\n1,2,3,4\n");

        document.Records[0].Fields.Count.ShouldBe(2);
        document.Records[1].Fields.Count.ShouldBe(4);
    }

    [Fact]
    public void An_unterminated_quote_runs_to_the_end_of_the_file_without_throwing()
    {
        var document = CsvReader.Read("a,b\n\"open,1\n2,3\n");

        document.Records.ShouldHaveSingleItem().Fields.ShouldHaveSingleItem().ShouldStartWith("open,1");
    }

    [Fact]
    public void Lone_carriage_returns_also_end_a_line()
    {
        var document = CsvReader.Read("a,b\r1,2\r3,4");

        document.Records.Count.ShouldBe(2);
        document.Records[1].Fields.ShouldBe(["3", "4"]);
    }
}