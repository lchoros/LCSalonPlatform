namespace Salon.Core.Messaging;

/// <summary>How long a text is on the wire.</summary>
/// <param name="Characters">What the client reads: letters, digits, spaces and line breaks.</param>
/// <param name="Segments">How many texts the network sends and bills for it; 0 for an empty message.</param>
/// <param name="IsUnicode">True when a character outside the basic phone alphabet forces the short, 70-character form.</param>
public readonly record struct SmsLength(int Characters, int Segments, bool IsUnicode);

/// <summary>
/// Counts the texts a message costs (GSM 03.38). Pure. A text in the basic phone alphabet holds 160 characters, or 153
/// each when it is split; a few signs (such as the euro sign and square brackets) count twice. One character outside
/// that alphabet, such as the Polish "ą" or "ł", switches the whole message to Unicode: 70 characters, or 67 each
/// when split. That is why a Polish text is short before it is expensive.
/// </summary>
public static class SmsLengthRules
{
    public const int SingleGsm = 160;
    public const int MultiGsm = 153;
    public const int SingleUnicode = 70;
    public const int MultiUnicode = 67;

    private const string Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";

    /// <summary>Signs of the extension table: each takes two places.</summary>
    private const string Extended = "^{}\\[~]|€\f";

    public static SmsLength Measure(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new SmsLength(0, 0, false);
        }

        // A browser sends line breaks as CR LF; a phone shows one break.
        var message = text.ReplaceLineEndings("\n");
        var septets = 0;
        foreach (var c in message)
        {
            if (Basic.Contains(c, StringComparison.Ordinal))
            {
                septets++;
            }
            else if (Extended.Contains(c, StringComparison.Ordinal))
            {
                septets += 2;
            }
            else
            {
                // UTF-16 code units, which is what the network counts in Unicode mode.
                return new SmsLength(message.Length, Segments(message.Length, SingleUnicode, MultiUnicode), true);
            }
        }

        return new SmsLength(message.Length, Segments(septets, SingleGsm, MultiGsm), false);
    }

    private static int Segments(int units, int single, int multi) => units <= single ? 1 : (units + multi - 1) / multi;
}