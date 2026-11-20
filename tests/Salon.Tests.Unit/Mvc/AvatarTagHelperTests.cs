using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Salon.Web.Framework.TagHelpers;
using Salon.Web.Framework.Ui;

namespace Salon.Tests.Unit.Mvc;

/// <summary>
/// The one face of the product (ADR 0088): a picture in a circle when there is an address, the first letter of the
/// name when there is none, and in both cases nothing a screen reader would say on top of the name beside it.
/// </summary>
public class AvatarTagHelperTests
{
    private static readonly Guid Anna = Guid.Parse("0196f3a0-0000-7000-8000-000000000001");

    [Fact]
    public void A_person_with_a_picture_is_a_round_image_with_an_empty_alt()
    {
        var html = Render(new AvatarTagHelper { Name = "Anna Kowalska", Photo = "/staff-photo/abc?v=1" });

        html.ShouldStartWith("<img src=\"/staff-photo/abc?v=1\" alt=\"\"");
        html.ShouldContain("data-avatar=\"photo\"");
        html.ShouldContain("rounded-full");
        html.ShouldContain("object-cover");
        html.ShouldContain("width=\"44\" height=\"44\"", customMessage: "sized in the markup too, for a page whose stylesheet has not arrived");
        html.ShouldNotContain("Anna", customMessage: "the name is beside it; saying it twice helps nobody");
        html.ShouldEndWith("/>");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_person_without_a_picture_is_their_first_letter_in_the_brand_circle(string? photo)
    {
        var html = Render(new AvatarTagHelper { Name = "tom Reed", Photo = photo });

        html.ShouldStartWith("<span ");
        html.ShouldEndWith(">T</span>");
        html.ShouldContain("aria-hidden=\"true\"");
        html.ShouldContain("data-avatar=\"letter\"");
        html.ShouldContain("bg-brand-soft");
        html.ShouldContain("text-brand");
        html.ShouldContain("rounded-full");
        html.ShouldNotContain("<img");
    }

    [Fact]
    public void A_name_is_written_as_text_never_as_markup()
    {
        Render(new AvatarTagHelper { Name = "<script>alert(1)</script>" }).ShouldEndWith(">&lt;</span>");
        Render(new AvatarTagHelper { Name = "   " }).ShouldEndWith(">·</span>");
    }

    [Theory]
    [InlineData(AvatarSize.Small, "size-7", "28")]
    [InlineData(AvatarSize.Medium, "size-11", "44")]
    [InlineData(AvatarSize.Large, "size-20", "80")]
    public void Three_sizes_for_a_pill_a_card_and_a_page(AvatarSize size, string box, string pixels)
    {
        Render(new AvatarTagHelper { Name = "Anna", Size = size }).ShouldContain(box);
        var picture = Render(new AvatarTagHelper { Name = "Anna", Photo = "/p", Size = size });
        picture.ShouldContain(box);
        picture.ShouldContain($"width=\"{pixels}\" height=\"{pixels}\"");
    }

    [Fact]
    public void Classes_given_in_the_view_are_kept()
    {
        var attributes = new TagHelperAttributeList { { "class", "mt-1" } };

        Render(new AvatarTagHelper { Name = "Anna" }, attributes).ShouldContain(" mt-1\"");
    }

    [Fact]
    public void The_address_of_a_picture_carries_its_version_and_nobody_without_one_has_an_address()
    {
        StaffPhotoAddress.For(Anna, "1795168800000").ShouldBe($"/staff-photo/{Anna}?v=1795168800000");
        StaffPhotoAddress.For(Anna, (string?)null).ShouldBeNull();
        StaffPhotoAddress.For(Anna, string.Empty).ShouldBeNull();

        var versions = new Dictionary<Guid, string> { [Anna] = "7" };
        StaffPhotoAddress.For(Anna, versions).ShouldBe($"/staff-photo/{Anna}?v=7");
        StaffPhotoAddress.For(Guid.NewGuid(), versions).ShouldBeNull();
        StaffPhotoAddress.Route.ShouldBe("staff-photo/{staffMemberId:guid}");
    }

    [Theory]
    [InlineData(true, true, false, "public, max-age=31536000, immutable")]
    [InlineData(true, true, true, "private, max-age=31536000, immutable")]
    [InlineData(true, false, false, "no-cache")]
    [InlineData(true, false, true, "private, no-cache")]
    [InlineData(false, true, true, "private, no-cache")]
    [InlineData(false, false, true, "private, no-cache")]
    [InlineData(false, true, false, "private, no-cache")]
    public void A_picture_is_kept_for_good_only_under_its_current_version_and_shared_only_when_nobody_is_signed_in(bool staffIsActive, bool isCurrentVersion, bool signedIn, string expected)
    {
        StaffPhotoAddress.CacheControlFor(staffIsActive, isCurrentVersion, signedIn).ShouldBe(expected);
    }

    private static string Render(AvatarTagHelper helper, TagHelperAttributeList? attributes = null)
    {
        var context = new TagHelperContext([], new Dictionary<object, object>(), "avatar");
        var output = new TagHelperOutput("avatar", attributes ?? [], (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()))
        {
            TagMode = TagMode.SelfClosing,
        };
        helper.Process(context, output);

        // The host's encoder: every letter is written as itself (Polish names), only markup characters are escaped.
        using var writer = new StringWriter();
        output.WriteTo(writer, HtmlEncoder.Create(UnicodeRanges.All));
        return writer.ToString();
    }
}