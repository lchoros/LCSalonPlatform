using System.Text;

namespace Salon.Plugin.Import.Csv;

/// <summary>
/// Bytes to text (ADR 0019): a byte-order mark wins (UTF-8, UTF-16 LE/BE); otherwise strict UTF-8; when that fails the file
/// is read as Windows-1250, the encoding Excel on a Polish PC saves CSV in. NUL bytes without a UTF-16 BOM mean binary
/// content (a zipped XLSX, a PDF) and are refused.
/// </summary>
public static class CsvDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Lazy<Encoding> Windows1250 = new(() =>
    {
        // The code-pages provider ships in the shared framework; registering twice is harmless.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1250);
    });

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string text)
    {
        text = string.Empty;
        if (bytes.IsEmpty)
        {
            return true;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
        {
            text = Encoding.Unicode.GetString(bytes[2..]);
            return true;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
        {
            text = Encoding.BigEndianUnicode.GetString(bytes[2..]);
            return true;
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            text = Encoding.UTF8.GetString(bytes[3..]);
            return true;
        }

        if (bytes.IndexOf((byte)0) >= 0)
        {
            return false;
        }

        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Windows1250.Value.GetString(bytes);
        }

        return true;
    }
}