using System.Globalization;
using NodaTime;

namespace Salon.Core.Staff;

/// <summary>Why a file is not taken as a staff photo.</summary>
public enum StaffPhotoProblem
{
    /// <summary>No file, or a file with nothing in it.</summary>
    Empty,

    /// <summary>More than <see cref="StaffPhotoRules.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary>The bytes are not a JPEG, a PNG or a WebP picture, whatever the file is called.</summary>
    NotAPicture,
}

/// <summary>
/// What counts as a staff photo (ADR 0088): a JPEG, PNG or WebP picture of at most 512 KB, told by the first bytes of
/// the file and never by its name or by what the browser says it is.
/// </summary>
public static class StaffPhotoRules
{
    /// <summary>Stored as uploaded and sent to every visitor of the booking page, so it has to stay small.</summary>
    public const int MaxBytes = MaxKilobytes * 1024;

    /// <summary>The cap as shown in messages.</summary>
    public const int MaxKilobytes = 512;

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";

    /// <summary>The types a file may be found to be; the list a file picker is given.</summary>
    public static IReadOnlyList<string> ContentTypes { get; } = [Jpeg, Png, WebP];

    /// <summary>The content type the first bytes say the file is, or null when it is none of the three.</summary>
    public static string? SniffContentType(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(JpegSignature))
        {
            return Jpeg;
        }

        if (content.StartsWith(PngSignature))
        {
            return Png;
        }

        // RIFF is a container for sound and video too: a WebP says so in bytes 8 to 11, after the four of its length.
        if (content.Length >= WebPMarkOffset + WebPMark.Length && content.StartsWith(RiffSignature) && content[WebPMarkOffset..].StartsWith(WebPMark))
        {
            return WebP;
        }

        return null;
    }

    /// <summary>Why the file is refused, or null when it may be stored. Size is judged first: a file too large is not read further.</summary>
    public static StaffPhotoProblem? Check(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return StaffPhotoProblem.Empty;
        }

        if (content.Length > MaxBytes)
        {
            return StaffPhotoProblem.TooLarge;
        }

        return SniffContentType(content) is null ? StaffPhotoProblem.NotAPicture : null;
    }

    private const int WebPMarkOffset = 8;

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;

    private static ReadOnlySpan<byte> WebPMark => "WEBP"u8;

    /// <summary>
    /// The version an address of the picture carries: it changes with every new picture, so a browser may keep what
    /// it fetched under one version for good.
    /// </summary>
    public static string VersionOf(Instant updatedAt) => updatedAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
}