using System.Buffers.Text;
using System.Security.Cryptography;

namespace Salon.Core.Booking;

/// <summary>
/// The secret in a client's cancel link (ADR 0014): 32 random bytes as base64url (43 characters, no padding), stored on
/// the appointment under a unique index. Knowing the token is the only credential needed to see and cancel that one
/// appointment, so it is never logged and only ever sent to the client's own phone or email.
/// </summary>
public static class PublicBookingToken
{
    public const int Bytes = 32;

    /// <summary>Length of the base64url text of <see cref="Bytes"/> bytes.</summary>
    public const int Length = 43;

    public static string Generate()
        => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Bytes));

    /// <summary>True for text of the right length and alphabet; a cheap guard before the database is asked.</summary>
    public static bool LooksValid(string? token)
        => token is { Length: Length } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
