using Salon.Services.Clients;

namespace Salon.Tests.Unit.Clients;

/// <summary>
/// ADR 0011: numbers are stored and searched in one shape (+ and digits). Local formats resolve only with the tenant's
/// country, taken from its currency; without it a number needs an explicit country code.
/// </summary>
public class PhoneNumberTests
{
    [Theory]
    [InlineData("+44 7700 900123", null, "+447700900123")]
    [InlineData("+44 (0) 7700-900123", "GBP", "+447700900123")]
    [InlineData("0044 7700 900123", null, "+447700900123")]
    [InlineData("+48 601.234.567", null, "+48601234567")]
    [InlineData("  +1 212 555 0199 ", "PLN", "+12125550199")]
    public void International_numbers_normalise_without_a_hint(string raw, string? currency, string expected)
    {
        PhoneNumber.Normalise(raw, currency).ShouldBe(expected);
        PhoneNumber.TryNormalise(raw, currency, out var normalised).ShouldBeTrue();
        normalised.ShouldBe(expected);
    }

    [Theory]
    [InlineData("07700 900123", "+447700900123")]
    [InlineData("07700-900-123", "+447700900123")]
    [InlineData("020 7946 0958", "+442079460958")]
    public void Uk_local_numbers_resolve_for_a_GBP_salon(string raw, string expected)
    {
        PhoneNumber.Normalise(raw, "GBP").ShouldBe(expected);
    }

    [Theory]
    [InlineData("601 234 567", "+48601234567")]
    [InlineData("512-345-678", "+48512345678")]
    [InlineData("(71) 234 56 78", "+48712345678")]
    [InlineData("881234567", "+48881234567")]
    public void Polish_local_numbers_resolve_for_a_PLN_salon(string raw, string expected)
    {
        PhoneNumber.Normalise(raw, "PLN").ShouldBe(expected);
    }

    [Theory]
    [InlineData("07700 900123", null)]
    [InlineData("07700 900123", "PLN")]
    [InlineData("601 234 567", null)]
    [InlineData("601 234 567", "GBP")]
    [InlineData("601 234 567", "EUR")]
    public void A_local_number_without_a_matching_country_is_refused(string raw, string? currency)
    {
        PhoneNumber.TryNormalise(raw, currency, out var normalised).ShouldBeFalse();
        normalised.ShouldBeNull();
        Should.Throw<FormatException>(() => PhoneNumber.Normalise(raw, currency));
    }

    [Theory]
    [InlineData("call me")]
    [InlineData("+44")]
    [InlineData("+")]
    [InlineData("12345")]
    [InlineData("+44 7700 900123 ext 4")]
    [InlineData("+4477009001234567890")]
    [InlineData("0770090012")]
    [InlineData("401234567")]
    public void Garbage_is_refused(string raw)
    {
        PhoneNumber.TryNormalise(raw, "GBP", out _).ShouldBeFalse();
        PhoneNumber.TryNormalise(raw, "PLN", out _).ShouldBeFalse();
        Should.Throw<FormatException>(() => PhoneNumber.Normalise(raw, "GBP"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_normalises_to_null(string? raw)
    {
        PhoneNumber.TryNormalise(raw, "GBP", out var normalised).ShouldBeTrue();
        normalised.ShouldBeNull();
        PhoneNumber.Normalise(raw, "GBP").ShouldBeNull();
    }

    [Theory]
    [InlineData("+44 7700 900123", "447700900123")]
    [InlineData("0770", "0770")]
    [InlineData("(0) 77", "077")]
    [InlineData("abc", "")]
    [InlineData(null, "")]
    public void Digits_keeps_only_digits_for_a_search(string? raw, string expected)
    {
        PhoneNumber.Digits(raw).ShouldBe(expected);
    }
}