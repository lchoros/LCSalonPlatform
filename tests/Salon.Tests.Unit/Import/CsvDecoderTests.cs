using System.Text;

using Salon.Plugin.Import.Csv;

namespace Salon.Tests.Unit.Import;

/// <summary>ADR 0019: BOM wins, then strict UTF-8, then Windows-1250 for Polish files saved by Excel; binary is refused.</summary>
public class CsvDecoderTests
{
    [Fact]
    public void Strips_a_utf8_bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("Imię,Nazwisko")).ToArray();

        CsvDecoder.TryDecode(bytes, out var text).ShouldBeTrue();

        text.ShouldBe("Imię,Nazwisko");
    }

    [Fact]
    public void Reads_plain_utf8()
    {
        CsvDecoder.TryDecode(Encoding.UTF8.GetBytes("Zażółć gęślą jaźń"), out var text).ShouldBeTrue();

        text.ShouldBe("Zażółć gęślą jaźń");
    }

    [Fact]
    public void Falls_back_to_windows_1250_when_the_bytes_are_not_utf8()
    {
        // "Małgorzata Łódźka" as Excel writes it on a Polish PC.
        var bytes = new byte[] { (byte)'M', (byte)'a', 0xB3, (byte)'g', (byte)'o', (byte)'r', (byte)'z', (byte)'a', (byte)'t', (byte)'a', (byte)' ', 0xA3, 0xF3, (byte)'d', 0x9F, (byte)'k', (byte)'a' };

        CsvDecoder.TryDecode(bytes, out var text).ShouldBeTrue();

        text.ShouldBe("Małgorzata Łódźka");
    }

    [Fact]
    public void Decodes_the_versum_fixture_with_every_polish_letter_intact()
    {
        CsvDecoder.TryDecode(ImportFixtures.Read(ImportFixtures.VersumCp1250), out var text).ShouldBeTrue();

        text.ShouldContain("Imię;Nazwisko;Telefon komórkowy");
        text.ShouldContain("Małgorzata;Łódźka");
        text.ShouldContain("Źrebięcki");
        text.ShouldContain("Śliwińska");
        text.ShouldContain("Lubi kawę z mlekiem; woli popołudnia");
        text.ShouldNotContain("�");
    }

    [Fact]
    public void Reads_utf16_with_a_bom()
    {
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Ab,Cd")).ToArray();

        CsvDecoder.TryDecode(bytes, out var text).ShouldBeTrue();

        text.ShouldBe("Ab,Cd");
    }

    [Fact]
    public void Refuses_binary_content()
    {
        var bytes = new byte[] { (byte)'P', (byte)'K', 0x03, 0x04, 0x00, 0x00, (byte)'a' };

        CsvDecoder.TryDecode(bytes, out var text).ShouldBeFalse();

        text.ShouldBe(string.Empty);
    }

    [Fact]
    public void Empty_input_is_empty_text()
    {
        CsvDecoder.TryDecode([], out var text).ShouldBeTrue();

        text.ShouldBe(string.Empty);
    }
}
