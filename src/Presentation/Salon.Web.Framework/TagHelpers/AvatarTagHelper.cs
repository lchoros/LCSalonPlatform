using System.Globalization;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Salon.Web.Framework.Ui;

namespace Salon.Web.Framework.TagHelpers;

/// <summary>How large the circle is: inside a pill, on a list card, or as the picture of a page.</summary>
public enum AvatarSize
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// <c>&lt;avatar name="Anna Kowalska" photo="/staff-photo/…" /&gt;</c> renders a person's face as a circle (ADR 0088):
/// the picture when <see cref="Photo"/> has an address, otherwise the first letter of the name in the brand's soft
/// circle (the look of <c>.service-dot</c>). Every face in the product is this one element. It always stands next to
/// the visible name, so it is decorative: the picture's <c>alt</c> is empty and the letter is hidden from a screen
/// reader, which would otherwise read the name twice.
/// </summary>
[HtmlTargetElement("avatar", TagStructure = TagStructure.WithoutEndTag)]
public sealed class AvatarTagHelper : TagHelper
{
    private const string Circle = "shrink-0 rounded-full bg-brand-soft";
    private const string Picture = Circle + " object-cover";
    private const string Letter = Circle + " inline-flex items-center justify-center font-bold text-brand";

    /// <summary>Whose face it is; the letter comes from it (<see cref="Monogram"/>).</summary>
    public string? Name { get; set; }

    /// <summary>The address of the picture (<see cref="StaffPhotoAddress"/>); null or empty shows the letter.</summary>
    public string? Photo { get; set; }

    public AvatarSize Size { get; set; } = AvatarSize.Medium;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var (box, text, pixels) = Size switch
        {
            AvatarSize.Small => ("size-7", "text-xs", 28),
            AvatarSize.Large => ("size-20", "text-2xl", 80),
            _ => ("size-11", "text-base", 44),
        };
        var extra = output.Attributes.TryGetAttribute("class", out var given) ? " " + given.Value : string.Empty;

        if (string.IsNullOrEmpty(Photo))
        {
            output.TagName = "span";
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute("class", $"{Letter} {box} {text}{extra}");
            output.Attributes.SetAttribute("aria-hidden", "true");
            output.Attributes.SetAttribute("data-avatar", "letter");
            output.Content.SetContent(Monogram.For(Name));
            return;
        }

        var side = pixels.ToString(CultureInfo.InvariantCulture);
        output.TagName = "img";
        output.TagMode = TagMode.SelfClosing;
        output.Attributes.SetAttribute("src", Photo);
        output.Attributes.SetAttribute("alt", string.Empty);
        // The size is also in the markup, so a page whose stylesheet has not arrived does not show the file full size.
        output.Attributes.SetAttribute("width", side);
        output.Attributes.SetAttribute("height", side);
        output.Attributes.SetAttribute("decoding", "async");
        output.Attributes.SetAttribute("class", $"{Picture} {box}{extra}");
        output.Attributes.SetAttribute("data-avatar", "photo");
    }
}