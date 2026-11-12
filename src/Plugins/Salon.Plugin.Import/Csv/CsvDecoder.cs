namespace Salon.Plugin.Import.Csv;

/// <summary>Bytes to text: BOM, strict UTF-8, then Windows-1250 (ADR 0019).</summary>
public static class CsvDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text) => throw new NotImplementedException();
}
