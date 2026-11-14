using Salon.Core.Booking;

namespace Salon.Tests.Unit.Booking;

public class PublicBookingTokenTests
{
    [Fact]
    public void A_token_is_22_url_safe_characters_from_128_random_bits_and_tokens_differ()
    {
        var tokens = Enumerable.Range(0, 200).Select(_ => PublicBookingToken.Generate()).ToList();

        PublicBookingToken.Bytes.ShouldBe(16, "128 bits: short enough for one text, far too long to guess (ADR 0031)");
        PublicBookingToken.Length.ShouldBe(22);
        tokens.ShouldAllBe(t => t.Length == PublicBookingToken.Length);
        tokens.ShouldAllBe(t => PublicBookingToken.LooksValid(t));
        tokens.ShouldAllBe(t => Uri.EscapeDataString(t) == t, "base64url needs no escaping in a path");
        tokens.Distinct(StringComparer.Ordinal).Count().ShouldBe(tokens.Count);
    }

    [Fact]
    public void A_token_given_before_the_links_got_shorter_still_looks_valid()
    {
        PublicBookingToken.LegacyLength.ShouldBe(43);
        PublicBookingToken.LooksValid(new string('A', 43)).ShouldBeTrue();
        PublicBookingToken.LooksValid("abcDEF123-_abcDEF123-_abcDEF123-_abcDEF1234").ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA+")]
    [InlineData("AAAAAAAAAAAAAAAAAAAA A")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Padding_slashes_spaces_and_other_lengths_do_not_look_like_a_token(string? token)
    {
        PublicBookingToken.LooksValid(token).ShouldBeFalse();
    }
}