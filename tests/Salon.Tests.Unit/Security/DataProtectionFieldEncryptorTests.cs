using System.Security.Cryptography;

using Microsoft.AspNetCore.DataProtection;

using Salon.Core.Clients;
using Salon.Web.Framework.Security;

namespace Salon.Tests.Unit.Security;

/// <summary>Health notes at rest (ADR 0011): authenticated encryption through Data Protection, a fresh IV per call, and no silent garbage on the way back.</summary>
public class DataProtectionFieldEncryptorTests
{
    private const string Plain = "Allergic to PPD; patch test 2026-10-01 negative. Zażółć gęślą jaźń.";

    private readonly DataProtectionFieldEncryptor _encryptor = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void A_round_trip_gives_the_text_back()
    {
        var cipher = _encryptor.Protect(Plain);

        _encryptor.Unprotect(cipher).ShouldBe(Plain);
        _encryptor.TryUnprotect(cipher, out var plain).ShouldBeTrue();
        plain.ShouldBe(Plain);
    }

    [Fact]
    public void Cipher_text_is_not_the_plain_text_and_differs_between_two_encryptions()
    {
        var first = _encryptor.Protect(Plain);
        var second = _encryptor.Protect(Plain);

        first.ShouldNotBe(Plain);
        first.ShouldNotContain("PPD");
        first.ShouldNotContain("patch");
        first.ShouldNotBe(second);
        _encryptor.Unprotect(second).ShouldBe(Plain);
    }

    [Fact]
    public void Cipher_text_fits_the_column_for_the_longest_allowed_note()
    {
        var longest = new string('ż', Client.HealthNotesMaxLength);

        _encryptor.Protect(longest).Length.ShouldBeLessThanOrEqualTo(Client.HealthNotesProtectedMaxLength);
    }

    [Fact]
    public void Another_key_ring_cannot_read_it()
    {
        var cipher = _encryptor.Protect(Plain);
        var other = new DataProtectionFieldEncryptor(new EphemeralDataProtectionProvider());

        other.TryUnprotect(cipher, out var plain).ShouldBeFalse();
        plain.ShouldBeNull();
        Should.Throw<CryptographicException>(() => other.Unprotect(cipher));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-cipher-text")]
    [InlineData("QUJDREVGRw==")]
    public void Garbage_does_not_unprotect(string cipher)
    {
        _encryptor.TryUnprotect(cipher, out var plain).ShouldBeFalse();
        plain.ShouldBeNull();
    }

    [Fact]
    public void Empty_text_cannot_be_protected()
    {
        Should.Throw<ArgumentException>(() => _encryptor.Protect(string.Empty));
    }
}