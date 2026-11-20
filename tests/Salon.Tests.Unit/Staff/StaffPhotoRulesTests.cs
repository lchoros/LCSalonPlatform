using System.Text;
using NodaTime;
using Salon.Core.Staff;
using Salon.Core.Tenancy;

namespace Salon.Tests.Unit.Staff;

/// <summary>
/// What a staff photo may be (ADR 0088): the first bytes decide, never the file name; at most 512 KB; and the version
/// an address carries changes with every new picture.
/// </summary>
public class StaffPhotoRulesTests
{
    private static readonly byte[] JpegStart = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] PngStart = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] WebPStart = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBP"u8, .. "VP8 "u8];

    [Fact]
    public void A_jpeg_a_png_and_a_webp_are_told_by_their_first_bytes()
    {
        StaffPhotoRules.SniffContentType(JpegStart).ShouldBe("image/jpeg");
        StaffPhotoRules.SniffContentType(PngStart).ShouldBe("image/png");
        StaffPhotoRules.SniffContentType(WebPStart).ShouldBe("image/webp");
    }

    [Fact]
    public void Text_and_other_kinds_of_file_are_no_picture()
    {
        StaffPhotoRules.SniffContentType("just some text, renamed photo.jpg"u8).ShouldBeNull();
        StaffPhotoRules.SniffContentType("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8).ShouldBeNull("an SVG can carry script");
        StaffPhotoRules.SniffContentType("<html><body>hello</body></html>"u8).ShouldBeNull();
        StaffPhotoRules.SniffContentType("GIF89a\u0001\u0000\u0001\u0000"u8).ShouldBeNull("a GIF is not one of the three");
        StaffPhotoRules.SniffContentType("%PDF-1.7 and the rest"u8).ShouldBeNull();
        StaffPhotoRules.SniffContentType([.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WAVE"u8, .. "fmt "u8]).ShouldBeNull("RIFF is also sound and video; only RIFF…WEBP is a picture");
    }

    [Fact]
    public void A_file_cut_short_before_its_signature_ends_is_no_picture()
    {
        StaffPhotoRules.SniffContentType([]).ShouldBeNull();
        StaffPhotoRules.SniffContentType(JpegStart.AsSpan(0, 2)).ShouldBeNull();
        StaffPhotoRules.SniffContentType(PngStart.AsSpan(0, 7)).ShouldBeNull();
        StaffPhotoRules.SniffContentType(WebPStart.AsSpan(0, 11)).ShouldBeNull();
    }

    [Fact]
    public void The_signature_must_be_at_the_very_start()
    {
        byte[] shifted = [0x20, .. JpegStart];

        StaffPhotoRules.SniffContentType(shifted).ShouldBeNull();
    }

    [Fact]
    public void A_picture_of_exactly_512_KB_is_taken_and_one_byte_more_is_too_large()
    {
        StaffPhotoRules.MaxBytes.ShouldBe(512 * 1024);
        StaffPhotoRules.MaxKilobytes.ShouldBe(512);

        StaffPhotoRules.Check(Picture(PngStart, StaffPhotoRules.MaxBytes)).ShouldBeNull();
        StaffPhotoRules.Check(Picture(PngStart, StaffPhotoRules.MaxBytes + 1)).ShouldBe(StaffPhotoProblem.TooLarge);
    }

    [Fact]
    public void Nothing_at_all_is_empty_and_a_small_text_file_is_no_picture()
    {
        StaffPhotoRules.Check([]).ShouldBe(StaffPhotoProblem.Empty);
        StaffPhotoRules.Check(Encoding.UTF8.GetBytes("name,phone\nAnna,07700 900123")).ShouldBe(StaffPhotoProblem.NotAPicture);
        StaffPhotoRules.Check(JpegStart).ShouldBeNull();
    }

    [Fact]
    public void A_file_that_is_too_large_is_refused_for_its_size_whatever_it_is()
    {
        var text = new byte[StaffPhotoRules.MaxBytes + 1];
        Array.Fill(text, (byte)'a');

        StaffPhotoRules.Check(text).ShouldBe(StaffPhotoProblem.TooLarge);
    }

    [Fact]
    public void The_file_picker_is_offered_exactly_the_three_kinds()
    {
        StaffPhotoRules.ContentTypes.ShouldBe(["image/jpeg", "image/png", "image/webp"]);
    }

    [Fact]
    public void The_version_changes_with_every_new_picture_and_reads_the_same_in_any_language()
    {
        var first = Instant.FromUtc(2026, 11, 20, 10, 0, 0);

        StaffPhotoRules.VersionOf(first).ShouldBe("1795168800000");
        StaffPhotoRules.VersionOf(first + Duration.FromMilliseconds(1)).ShouldNotBe(StaffPhotoRules.VersionOf(first));
    }

    [Fact]
    public void A_photo_is_tenant_owned_and_holds_no_file_name()
    {
        var photo = new StaffPhoto { ContentType = StaffPhotoRules.Png, Bytes = PngStart };

        photo.ShouldBeAssignableTo<ITenantEntity>();
        photo.Id.Version.ShouldBe(7);
        typeof(StaffPhoto).GetProperties().Select(p => p.Name).ShouldNotContain("FileName", "the name of a file can say who took it or where; it is never kept");
    }

    private static byte[] Picture(byte[] start, int length)
    {
        var bytes = new byte[length];
        start.CopyTo(bytes, 0);
        return bytes;
    }
}