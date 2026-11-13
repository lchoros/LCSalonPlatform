using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

using Salon.Core.Locations;
using Salon.Services.Locations;
using Salon.Services.Notifications;

namespace Salon.Tests.Unit.Locations;

/// <summary>How an address and a phone number are checked and written out (ADR 0027), and how an email ends with them.</summary>
public class LocationRulesTests
{
    [Theory]
    [InlineData("020 7946 0020")]
    [InlineData("+44 20 7946 0020")]
    [InlineData("(020) 7946-0020")]
    [InlineData("22 123 45 67")]
    [InlineData("+48.22.123.45.67")]
    [InlineData("  07700900123  ")]
    public void A_phone_number_is_digits_with_the_usual_separators_and_an_optional_plus_in_front(string phone)
    {
        LocationRules.IsPhone(phone).ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345")]
    [InlineData("1234567890123456")]
    [InlineData("020 7946 CALL")]
    [InlineData("020+7946 0020")]
    [InlineData("020 7946 0020 ext 3")]
    [InlineData("<script>0207946002</script>")]
    public void Anything_else_is_not_a_phone_number(string? phone)
    {
        LocationRules.IsPhone(phone).ShouldBeFalse();
        LocationRules.Dialable(phone).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("020 7946 0020", "02079460020")]
    [InlineData("+44 (20) 7946-0020", "+442079460020")]
    [InlineData(" 22.123.45.67 ", "221234567")]
    public void The_dialable_form_keeps_the_digits_and_the_leading_plus(string phone, string expected)
    {
        LocationRules.Dialable(phone).ShouldBe(expected);
    }

    [Property]
    public Property A_dialable_number_never_holds_anything_but_digits_and_one_leading_plus(NonNull<string> text)
    {
        var dialable = LocationRules.Dialable(text.Get);

        return dialable.TrimStart('+').All(char.IsAsciiDigit).ToProperty()
            .And(dialable.LastIndexOf('+') <= 0);
    }

    [Fact]
    public void An_address_is_written_on_one_line_the_way_the_country_writes_it()
    {
        LocationRules.OneLine("12 High Street", null, "London", "N1 9GU", postcodeFirst: false).ShouldBe("12 High Street, London N1 9GU");
        LocationRules.OneLine(" 12 High Street ", " Floor 2 ", " London ", " N1 9GU ", postcodeFirst: false).ShouldBe("12 High Street, Floor 2, London N1 9GU");
        LocationRules.OneLine("ul. Piękna 12", "  ", "Warszawa", "00-549", postcodeFirst: true).ShouldBe("ul. Piękna 12, 00-549 Warszawa");
    }

    [Theory]
    [InlineData("pl-PL", true)]
    [InlineData("PL-pl", true)]
    [InlineData("en-GB", false)]
    [InlineData(null, false)]
    public void Poland_writes_the_postcode_before_the_town(string? culture, bool expected)
    {
        LocationRules.PostcodeFirst(culture).ShouldBe(expected);
    }

    [Fact]
    public void The_map_link_carries_the_address_and_nothing_else_safely_encoded()
    {
        LocationRules.MapUrl("12 High Street, London N1 9GU")
            .ShouldBe("https://www.google.com/maps/search/?api=1&query=12%20High%20Street%2C%20London%20N1%209GU");
        LocationRules.MapUrl("ul. Piękna 12 & \"x\"").ShouldBe("https://www.google.com/maps/search/?api=1&query=ul.%20Pi%C4%99kna%2012%20%26%20%22x%22");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData("  Studio  ", "Studio")]
    public void Blank_text_is_nothing_and_the_rest_is_trimmed(string? text, string? expected)
    {
        LocationRules.Normalize(text).ShouldBe(expected);
    }

    [Fact]
    public void The_model_offers_a_tel_link_only_for_a_number()
    {
        Location(phone: "020 7946 0020").PhoneLink.ShouldBe("tel:02079460020");
        Location(phone: null).PhoneLink.ShouldBeNull();
        Location(phone: null).MapUrl.ShouldEndWith("query=12%20High%20Street%2C%20London%20N1%209GU");
    }

    [Fact]
    public void An_email_ends_with_the_salon_name_the_address_and_the_phone_number()
    {
        AppointmentNotifier.WithFooter("Hi Olivia,\nsee you soon.\n", "Studio 20", Location(phone: "020 7946 0020"))
            .ShouldBe("Hi Olivia,\nsee you soon.\n\nStudio 20\n12 High Street, London N1 9GU\n020 7946 0020");
    }

    [Fact]
    public void The_footer_uses_the_name_of_the_place_when_it_has_one_and_leaves_out_what_is_missing()
    {
        AppointmentNotifier.WithFooter("Body", "Studio 20", Location(phone: null, name: "Studio 20 Shoreditch"))
            .ShouldBe("Body\n\nStudio 20 Shoreditch\n12 High Street, London N1 9GU");
    }

    [Fact]
    public void Without_a_location_the_email_is_left_as_it_was()
    {
        AppointmentNotifier.WithFooter("Body\n", "Studio 20", null).ShouldBe("Body\n");
    }

    private static SalonLocation Location(string? phone, string? name = null)
        => new(Guid.NewGuid(), name, "12 High Street", null, "London", "N1 9GU", phone, null, "12 High Street, London N1 9GU");
}