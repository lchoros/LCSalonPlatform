using System.Buffers.Text;
using System.Security.Cryptography;

namespace Salon.Core.Booking;

/// <summary>
/// The secret in a client's own link (ADR 0014, ADR 0031): 16 random bytes as base64url (22 characters, no padding),
/// stored on the appointment under a unique index. Knowing the token is the only credential needed to see, change and
/// cancel that one appointment, so it is never logged and only ever sent to the client's own phone or email.
/// 128 random bits cannot be guessed; the token is this short so that a confirmation with its link fits in one text.
/// Tokens given before ADR 0031 have 32 bytes (43 characters) and keep working.
/// </summary>
public static class PublicBookingToken
{
    public const int Bytes = 16;

    /// <summary>Length of the base64url text of <see cref="Bytes"/> bytes.</summary>
    public const int Length = 22;

    /// <summary>Length of a token given before ADR 0031 (32 bytes).</summary>
    public const int LegacyLength = 43;

    public static string Generate()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    /// <summary>True for text of a known length and the right alphabet; a cheap guard before the database is asked.</summary>
    public static bool LooksValid(string? token)
        => token is { Length: Length or LegacyLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}