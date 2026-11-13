using System.Text;

namespace Salon.Core.Locations;

/// <summary>
/// How an address and a phone number are checked and written out (ADR 0027). Pure. A phone number is kept as the salon
/// typed it; only its dialable form (digits and a leading plus) goes into a <c>tel:</c> link.
/// </summary>
public static class LocationRules
{
    public const int MinPhoneDigits = 6;
    public const int MaxPhoneDigits = 15;

    /// <summary>The trimmed text, or null when blank.</summary>
    public static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>
    /// True for a number written with digits, spaces, brackets, dots and hyphens, an optional plus in front, and between
    /// <see cref="MinPhoneDigits"/> and <see cref="MaxPhoneDigits"/> digits (the longest an international number gets).
    /// </summary>
    public static bool IsPhone(string? phone)
    {
        if (Normalize(phone) is not { } text)
        {
            return false;
        }

        var digits = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is >= '0' and <= '9')
            {
                digits++;
            }
            else if (c == '+' ? i != 0 : c is not (' ' or '(' or ')' or '-' or '.'))
            {
                return false;
            }
        }

        return digits is >= MinPhoneDigits and <= MaxPhoneDigits;
    }

    /// <summary>The number as a phone dials it: digits with the leading plus kept. Empty for a number that is not one.</summary>
    public static string Dialable(string? phone)
    {
        if (!IsPhone(phone))
        {
            return string.Empty;
        }

        var dialable = new StringBuilder();
        foreach (var c in phone!.Trim())
        {
            if (c is '+' or (>= '0' and <= '9'))
            {
                dialable.Append(c);
            }
        }

        return dialable.ToString();
    }

    /// <summary>
    /// The address on one line, the way it is written on an envelope: "12 High Street, Floor 2, London N1 9GU", or with
    /// <paramref name="postcodeFirst"/> the Polish way, "ul. Piękna 12, 00-549 Warszawa".
    /// </summary>
    public static string OneLine(string addressLine1, string? addressLine2, string city, string postcode, bool postcodeFirst)
    {
        ArgumentNullException.ThrowIfNull(addressLine1);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(postcode);

        var town = postcodeFirst ? new[] { Normalize(postcode), Normalize(city) } : [Normalize(city), Normalize(postcode)];
        var parts = new[] { Normalize(addressLine1), Normalize(addressLine2), string.Join(' ', town.Where(p => p is not null)) };
        return string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>Poland writes the postcode before the town; the United Kingdom after it.</summary>
    public static bool PostcodeFirst(string? culture) => string.Equals(culture, "pl-PL", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A link that opens the address in the reader's maps app. The address is the only thing sent, as a search; nothing
    /// about the client or the booking is in it.
    /// </summary>
    public static string MapUrl(string oneLineAddress)
    {
        ArgumentNullException.ThrowIfNull(oneLineAddress);
        return "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(oneLineAddress);
    }
}