using System.Globalization;
using System.Text;

namespace Salon.Plugin.Import.Parsers;

/// <summary>
/// One shape for header names and their aliases (ADR 0019): lower case, diacritics removed (<c>Imię</c> → <c>imie</c>,
/// <c>Łódź</c> → <c>lodz</c>), runs of anything that is not a letter or digit collapsed to one space and trimmed, so
/// <c>Numer telefonu</c>, <c>numer_telefonu</c>, <c>NUMER-TELEFONU</c> and <c> Numer telefonu </c> are the same key.
/// </summary>
internal static class HeaderKey
{
    public static string Of(string header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var builder = new StringBuilder(header.Length);
        var pendingSpace = false;
        foreach (var raw in header.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // The stroke of ł/Ł is not a combining mark, so FormD leaves it alone.
            var c = raw switch
            {
                'ł' => 'l',
                'Ł' => 'L',
                _ => raw,
            };

            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    public static HashSet<string> Set(params string[] aliases) => aliases.Select(Of).ToHashSet(StringComparer.Ordinal);
}