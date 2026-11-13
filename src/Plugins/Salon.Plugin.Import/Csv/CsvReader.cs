using System.Text;
using Salon.Core.Import;

namespace Salon.Plugin.Import.Csv;

/// <summary>
/// A small RFC 4180 reader (ADR 0019), tolerant where exports are sloppy: the separator (comma, semicolon or tab) is the
/// one that occurs most in the header line outside quotes; unquoted fields are trimmed, quoted fields kept verbatim;
/// blank lines are skipped; CRLF, LF and lone CR all end a line; an unterminated quote runs to the end of the file
/// instead of throwing. Records are returned ragged; the parser decides what a wrong field count means.
/// </summary>
public static class CsvReader
{
    private static readonly char[] CandidateSeparators = [',', ';', '\t'];

    /// <param name="maxRecords">Stop after this many data records; <see cref="CsvDocument.Truncated"/> says whether more followed.</param>
    public static CsvDocument Read(string text, int maxRecords = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(maxRecords);

        var position = 0;
        var line = 1;
        var separator = ',';
        IReadOnlyList<string>? header = null;
        var records = new List<ImportRecord>();
        var truncated = false;

        while (position < text.Length)
        {
            if (SkipBlankLine(text, ref position, ref line))
            {
                continue;
            }

            if (header is null)
            {
                separator = DetectSeparator(text, position);
                header = ReadRecord(text, ref position, ref line, separator);
                continue;
            }

            if (records.Count >= maxRecords)
            {
                truncated = true;
                break;
            }

            var startLine = line;
            records.Add(new ImportRecord(startLine, ReadRecord(text, ref position, ref line, separator)));
        }

        return new CsvDocument(separator, header ?? [], records, truncated);
    }

    /// <summary>Consumes a line that holds only whitespace (including an empty one) and reports whether it did.</summary>
    private static bool SkipBlankLine(string text, ref int position, ref int line)
    {
        var scan = position;
        while (scan < text.Length && text[scan] is not ('\r' or '\n'))
        {
            if (!char.IsWhiteSpace(text[scan]))
            {
                return false;
            }

            scan++;
        }

        position = scan;
        ConsumeLineBreak(text, ref position, ref line);
        return true;
    }

    private static char DetectSeparator(string text, int position)
    {
        var counts = new int[CandidateSeparators.Length];
        var inQuotes = false;
        for (var i = position; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c is '\r' or '\n')
                {
                    break;
                }

                var index = Array.IndexOf(CandidateSeparators, c);
                if (index >= 0)
                {
                    counts[index]++;
                }
            }
        }

        var best = 0;
        for (var i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[best])
            {
                best = i;
            }
        }

        return CandidateSeparators[best];
    }

    private static List<string> ReadRecord(string text, ref int position, ref int line, char separator)
    {
        var fields = new List<string>();
        while (true)
        {
            fields.Add(position < text.Length && text[position] == '"'
                ? ReadQuotedField(text, ref position, ref line, separator)
                : ReadUnquotedField(text, ref position, separator));

            if (position < text.Length && text[position] == separator)
            {
                position++;
                continue;
            }

            ConsumeLineBreak(text, ref position, ref line);
            return fields;
        }
    }

    private static string ReadUnquotedField(string text, ref int position, char separator)
    {
        var start = position;
        while (position < text.Length && text[position] != separator && text[position] is not ('\r' or '\n'))
        {
            position++;
        }

        return text.AsSpan(start, position - start).Trim().ToString();
    }

    private static string ReadQuotedField(string text, ref int position, ref int line, char separator)
    {
        var builder = new StringBuilder();
        position++; // opening quote
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '"')
            {
                if (position + 1 < text.Length && text[position + 1] == '"')
                {
                    builder.Append('"');
                    position += 2;
                    continue;
                }

                position++; // closing quote
                break;
            }

            if (c == '\r' && position + 1 < text.Length && text[position + 1] == '\n')
            {
                builder.Append("\r\n");
                position += 2;
                line++;
                continue;
            }

            if (c is '\r' or '\n')
            {
                line++;
            }

            builder.Append(c);
            position++;
        }

        // Tolerate `"a" ,b`: whatever sits between the closing quote and the separator is noise.
        while (position < text.Length && text[position] != separator && text[position] is not ('\r' or '\n'))
        {
            position++;
        }

        return builder.ToString();
    }

    private static void ConsumeLineBreak(string text, ref int position, ref int line)
    {
        if (position >= text.Length)
        {
            return;
        }

        if (text[position] == '\r')
        {
            position++;
            if (position < text.Length && text[position] == '\n')
            {
                position++;
            }

            line++;
        }
        else if (text[position] == '\n')
        {
            position++;
            line++;
        }
    }
}