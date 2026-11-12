using System.Text;

using Salon.Core.Import;
using Salon.Core.Plugins;
using Salon.Plugin.Import;
using Salon.Plugin.Import.Parsers;

namespace Salon.Tests.Unit.Import;

/// <summary>ADR 0019: the plugin as a whole — decoding, detection, caps and file-level errors.</summary>
public class CsvClientImportProviderTests
{
    private readonly CsvClientImportProvider _provider = new([new BooksyCsvParser(), new VersumCsvParser()]);

    [Fact]
    public void Is_the_import_plugin()
    {
        _provider.SystemName.ShouldBe("Import.Csv");
        _provider.DisplayNameKey.ShouldBe("Plugins.Import.Csv.DisplayName");
        _provider.Formats.ShouldBe(["Booksy", "Versum"]);
        _provider.ShouldBeAssignableTo<IPlugin>();
    }

    [Fact]
    public void Detects_a_booksy_export()
    {
        var result = _provider.Parse(ImportFixtures.Read(ImportFixtures.Booksy));

        result.IsSuccess.ShouldBeTrue();
        result.Format.ShouldBe("Booksy");
        result.Rows.Count.ShouldBe(5);
        result.Header[0].ShouldBe("First Name", "the BOM must not stick to the first header");
        result.Rows[0].FirstName.ShouldBe("Olivia");
    }

    [Fact]
    public void Detects_a_versum_export_in_windows_1250()
    {
        var result = _provider.Parse(ImportFixtures.Read(ImportFixtures.VersumCp1250));

        result.IsSuccess.ShouldBeTrue();
        result.Format.ShouldBe("Versum");
        result.Rows.Count.ShouldBe(4);
        result.Rows[0].FirstName.ShouldBe("Małgorzata");
        result.Rows[0].LastName.ShouldBe("Łódźka");
    }

    [Fact]
    public void A_broken_file_parses_with_every_row_flagged_instead_of_throwing()
    {
        var result = _provider.Parse(ImportFixtures.Read(ImportFixtures.Broken));

        result.IsSuccess.ShouldBeTrue();
        result.Format.ShouldBe("Booksy");
        result.Rows.Count.ShouldBe(3);
        result.Rows.ShouldAllBe(r => r.Problems.Contains(ImportProblem.WrongColumnCount));
    }

    [Fact]
    public void A_header_nobody_recognises_is_an_unknown_format_with_the_header_kept()
    {
        var result = _provider.Parse(Encoding.UTF8.GetBytes("Foo,Bar\n1,2\n"));

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldBe(ClientImportError.UnknownFormat);
        result.Header.ShouldBe(["Foo", "Bar"]);
        result.Rows.ShouldBeEmpty();
        result.Format.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("First Name,Last Name,Phone\n")]
    public void An_empty_file_or_a_header_without_rows_is_empty(string text)
    {
        var result = _provider.Parse(Encoding.UTF8.GetBytes(text));

        result.Error.ShouldBe(ClientImportError.Empty);
        result.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Binary_content_is_refused()
    {
        var result = _provider.Parse(new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });

        result.Error.ShouldBe(ClientImportError.NotText);
    }

    [Fact]
    public void A_file_over_the_byte_cap_is_refused_before_decoding()
    {
        var bytes = new byte[ClientImportLimits.MaxBytes + 1];
        Array.Fill(bytes, (byte)'a');

        _provider.Parse(bytes).Error.ShouldBe(ClientImportError.TooLarge);
    }

    [Fact]
    public void Exactly_the_row_cap_is_accepted_and_one_more_is_refused()
    {
        _provider.Parse(FileWithRows(ClientImportLimits.MaxRows)).Rows.Count.ShouldBe(ClientImportLimits.MaxRows);

        var tooMany = _provider.Parse(FileWithRows(ClientImportLimits.MaxRows + 1));
        tooMany.Error.ShouldBe(ClientImportError.TooManyRows);
        tooMany.Rows.ShouldBeEmpty();
    }

    [Fact]
    public void Mixed_language_header_goes_to_the_parser_that_recognises_more_of_it()
    {
        // Booksy's signature columns outweigh the shared Polish name columns.
        var booksyPl = _provider.Parse(Encoding.UTF8.GetBytes("Imię,Nazwisko,Numer telefonu,Adres e-mail,Birthday,Zip Code,Accepts Marketing\nAnna,Nowak,601234567,a@b.pl,,,Yes\n"));
        booksyPl.Format.ShouldBe("Booksy");

        var versum = _provider.Parse(Encoding.UTF8.GetBytes("Imię;Nazwisko;Telefon komórkowy;E-mail;Opis;Zgoda na marketing\nAnna;Nowak;601234567;a@b.pl;;Tak\n"));
        versum.Format.ShouldBe("Versum");
    }

    private static byte[] FileWithRows(int count)
    {
        var builder = new StringBuilder("First Name,Last Name,Phone\n");
        for (var i = 0; i < count; i++)
        {
            builder.Append("Anna,Nowak,+4477009").Append(i.ToString("00000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
