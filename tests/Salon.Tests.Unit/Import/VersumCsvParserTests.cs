using Salon.Core.Import;
using Salon.Plugin.Import.Csv;
using Salon.Plugin.Import.Parsers;

namespace Salon.Tests.Unit.Import;

/// <summary>ADR 0019: the assumed Versum client export — Polish headers, semicolons, Windows-1250.</summary>
public class VersumCsvParserTests
{
    private readonly VersumCsvParser _parser = new();

    [Fact]
    public void Parses_the_polish_sample_export_with_its_letters_intact()
    {
        CsvDecoder.TryDecode(ImportFixtures.Read(ImportFixtures.VersumCp1250), out var text).ShouldBeTrue();
        var document = CsvReader.Read(text);
        document.Separator.ShouldBe(';');

        var rows = _parser.Parse(document.Header, document.Records);

        rows.Count.ShouldBe(4);

        var malgorzata = rows[0];
        malgorzata.SourceRow.ShouldBe(2);
        malgorzata.FirstName.ShouldBe("Małgorzata");
        malgorzata.LastName.ShouldBe("Łódźka");
        malgorzata.Phone.ShouldBe("601 234 567");
        malgorzata.Email.ShouldBe("malgorzata.lodzka@example.pl");
        malgorzata.Notes.ShouldBe("Lubi kawę z mlekiem; woli popołudnia");
        malgorzata.MarketingConsent.ShouldBeTrue();
        malgorzata.Problems.ShouldBeEmpty();

        var zbigniew = rows[1];
        zbigniew.LastName.ShouldBe("Źrebięcki");
        zbigniew.Phone.ShouldBe("+48 512 345 678");
        zbigniew.Email.ShouldBeNull();
        zbigniew.Notes.ShouldBeNull();
        zbigniew.MarketingConsent.ShouldBeFalse();

        var agnieszka = rows[2];
        agnieszka.LastName.ShouldBe("Śliwińska");
        agnieszka.Email.ShouldBe("Agnieszka.Sliwinska@Example.pl");
        agnieszka.Notes.ShouldBe("Stała klientka");
        agnieszka.MarketingConsent.ShouldBeTrue("'TAK' is a yes");

        var jan = rows[3];
        jan.SourceRow.ShouldBe(5);
        jan.FirstName.ShouldBe("Jan");
        jan.Problems.ShouldBe([ImportProblem.MissingLastName]);
    }

    [Fact]
    public void Matches_the_polish_header_and_the_english_one()
    {
        _parser.Match(["Imię", "Nazwisko", "Telefon komórkowy", "E-mail", "Data urodzenia", "Płeć", "Opis", "Zgoda marketingowa", "Źródło", "Ostatnia wizyta"]).ShouldBeGreaterThan(0);
        _parser.Match(["First name", "Last name", "Phone", "Email", "Description", "Marketing consent"]).ShouldBeGreaterThan(0);
        _parser.Match(["Foo", "Bar"]).ShouldBe(0);
        _parser.Match(["Imię", "Nazwisko"]).ShouldBe(0, "no contact column");
    }

    [Theory]
    [InlineData("Telefon")]
    [InlineData("Nr telefonu")]
    [InlineData("Telefon 1")]
    [InlineData("telefon_komorkowy")]
    public void Phone_column_aliases(string phoneHeader)
    {
        string[] header = ["Imię", "Nazwisko", phoneHeader];

        var row = _parser.Parse(header, [new ImportRecord(2, ["Anna", "Nowak", "601234567"])]).ShouldHaveSingleItem();

        row.Phone.ShouldBe("601234567");
    }

    [Theory]
    [InlineData("Opis")]
    [InlineData("Uwagi")]
    [InlineData("Komentarz")]
    [InlineData("Notatki")]
    public void Notes_column_aliases(string notesHeader)
    {
        string[] header = ["Imię", "Nazwisko", "Telefon", notesHeader];

        var row = _parser.Parse(header, [new ImportRecord(2, ["Anna", "Nowak", "601234567", "woli rano"])]).ShouldHaveSingleItem();

        row.Notes.ShouldBe("woli rano");
    }

    [Theory]
    [InlineData("Zgoda marketingowa")]
    [InlineData("Zgoda na marketing")]
    [InlineData("Zgoda SMS")]
    public void Consent_column_aliases(string consentHeader)
    {
        string[] header = ["Imię", "Nazwisko", "Telefon", consentHeader];

        var row = _parser.Parse(header, [new ImportRecord(2, ["Anna", "Nowak", "601234567", "Tak"])]).ShouldHaveSingleItem();

        row.MarketingConsent.ShouldBeTrue();
    }

    [Fact]
    public void The_format_name_is_stable()
    {
        _parser.Format.ShouldBe("Versum");
    }
}
