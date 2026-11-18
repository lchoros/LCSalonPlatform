using System.Text;
using Salon.Plugin.Import;
using Salon.Plugin.Import.Parsers;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0051: a client file written for a spreadsheet puts an apostrophe in front of a phone number that starts with a
/// plus. The import reads such a number back without the apostrophe, and leaves every other cell exactly as it was
/// typed: nothing can tell a guard from what somebody meant to write.
/// </summary>
public class SpreadsheetGuardTests
{
    private readonly CsvClientImportProvider _provider = new([new BooksyCsvParser(), new VersumCsvParser()]);

    [Fact]
    public void A_phone_number_guarded_for_a_spreadsheet_is_read_without_its_apostrophe()
    {
        var result = Parse("First name,Last name,Phone,Email\nOlivia,Brown,'+447700900123,olivia@example.test\n");

        result.Rows.ShouldHaveSingleItem().Phone.ShouldBe("+447700900123");
    }

    [Theory]
    [InlineData("'=likes tea")]
    [InlineData("'-x")]
    [InlineData("'+1 for colour")]
    [InlineData("'@home")]
    [InlineData("'")]
    public void A_note_is_kept_exactly_as_typed_apostrophe_and_all(string note)
    {
        var result = Parse($"First name,Last name,Phone,Notes\nOlivia,Brown,07700 900123,{note}\n");

        result.Rows.ShouldHaveSingleItem().Notes.ShouldBe(note);
    }

    [Fact]
    public void A_name_keeps_its_apostrophe_wherever_it_is()
    {
        var result = Parse("First name,Last name,Phone\n'Olivia,O'Brien,07700 900123\n");

        var row = result.Rows.ShouldHaveSingleItem();
        row.FirstName.ShouldBe("'Olivia");
        row.LastName.ShouldBe("O'Brien");
    }

    private Salon.Core.Import.ClientImportParseResult Parse(string csv) => _provider.Parse(Encoding.UTF8.GetBytes(csv));
}