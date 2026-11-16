using System.Text;
using Salon.Plugin.Import;
using Salon.Plugin.Import.Parsers;

namespace Salon.Tests.Unit.Import;

/// <summary>
/// ADR 0051: a client file written for a spreadsheet puts an apostrophe in front of text that would run as a formula,
/// such as a phone number that starts with a plus. The import reads such a file back without the apostrophe, and
/// leaves every other apostrophe alone.
/// </summary>
public class SpreadsheetGuardTests
{
    private readonly CsvClientImportProvider _provider = new([new BooksyCsvParser(), new VersumCsvParser()]);

    [Fact]
    public void A_phone_number_guarded_for_a_spreadsheet_is_read_without_its_apostrophe()
    {
        var result = Parse("First name,Last name,Phone,Email,Notes\nOlivia,Brown,'+447700900123,olivia@example.test,'=likes tea\n");

        var row = result.Rows.ShouldHaveSingleItem();
        row.Phone.ShouldBe("+447700900123");
        row.Notes.ShouldBe("=likes tea");
    }

    [Fact]
    public void Any_other_apostrophe_is_part_of_the_text()
    {
        var result = Parse("First name,Last name,Phone,Notes\n'Olivia,O'Brien,07700 900123,'\n");

        var row = result.Rows.ShouldHaveSingleItem();
        row.FirstName.ShouldBe("'Olivia");
        row.LastName.ShouldBe("O'Brien");
        row.Notes.ShouldBe("'");
    }

    private Salon.Core.Import.ClientImportParseResult Parse(string csv) => _provider.Parse(Encoding.UTF8.GetBytes(csv));
}