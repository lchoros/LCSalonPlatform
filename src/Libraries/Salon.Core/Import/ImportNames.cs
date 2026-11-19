using System.Globalization;
using System.Text;

namespace Salon.Core.Import;

/// <summary>
/// Names as an import compares them (ADR 0067): without case, accents or punctuation, one space between words, so
/// "Łucja  Kowalska" and "lucja kowalska" are one name and "Cut & Blow-Dry" and "cut & blow dry" one service. Pure.
/// </summary>
public static class ImportNames
{
    /// <summary>Lower case, accents and the Polish ł taken off, anything that is not a letter or a digit a space, spaces collapsed.</summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var raw in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(raw) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // The stroke of ł is not a combining mark, so the decomposition leaves it alone.
            var c = raw is 'ł' or 'Ł' ? 'l' : raw;
            if (char.IsLetterOrDigit(c))
            {
                if (space && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                space = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                space = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>The <see cref="Key"/> without its spaces: "blowdry" for "Blow-dry" and "blow dry", for a suggestion only.</summary>
    public static string Loose(string? text) => Key(text).Replace(" ", string.Empty, StringComparison.Ordinal);

    /// <summary>The words of a name as keys.</summary>
    public static IReadOnlyList<string> Words(string? text) => Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Whether a name written in a file can be the person on a card found by phone or email: the file names the card's
    /// first name. "Sarah", "Sarah Smith", "Smith Sarah" and "Sarah Brown" (a new surname) are Sarah Smith; "Lily Smith"
    /// is not, though a family shares a surname and often a phone. A file with no name says nothing against the card.
    /// </summary>
    public static bool SamePerson(string? written, string firstName)
    {
        var words = Words(written);
        if (words.Count == 0)
        {
            return true;
        }

        var first = Words(firstName);
        return first.Count > 0 && first.All(words.Contains);
    }

    /// <summary>Whether a name written in a file is exactly the card's full name, in either order.</summary>
    public static bool FullName(string? written, string firstName, string lastName)
    {
        var key = Key(written);
        return key.Length > 0 && (key == Key($"{firstName} {lastName}") || key == Key($"{lastName} {firstName}"));
    }
}