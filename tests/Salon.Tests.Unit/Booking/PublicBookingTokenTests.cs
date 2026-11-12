using Salon.Core.Booking;

namespace Salon.Tests.Unit.Booking;

public class PublicBookingTokenTests
{
    [Fact]
    public void A_token_is_43_url_safe_characters_and_tokens_differ()
    {
        var tokens = Enumerable.Range(0, 20).Select(_ => PublicBookingToken.Generate()).ToList();

        tokens.ShouldAllBe(t => t.Length == PublicBookingToken.Length);
        tokens.ShouldAllBe(t => PublicBookingToken.LooksValid(t));
        tokens.ShouldAllBe(t => Uri.EscapeDataString(t) == t, "base64url needs no escaping in a path");
        tokens.Distinct(StringComparer.Ordinal).Count().ShouldBe(tokens.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA/")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Padding_slashes_and_wrong_lengths_do_not_look_like_a_token(string? token)
    {
        PublicBookingToken.LooksValid(token).ShouldBeFalse();
    }
}