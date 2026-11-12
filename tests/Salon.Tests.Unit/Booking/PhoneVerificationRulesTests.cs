using NodaTime;

using Salon.Core.Booking;

namespace Salon.Tests.Unit.Booking;

/// <summary>The pure SMS-code rules of ADR 0014: random six digits, salted hash, constant-time check, five attempts, ten minutes.</summary>
public class PhoneVerificationRulesTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 12, 10, 0);

    [Fact]
    public void A_code_is_six_digits_and_two_codes_differ()
    {
        var codes = Enumerable.Range(0, 50).Select(_ => PhoneVerificationRules.GenerateCode()).ToList();

        codes.ShouldAllBe(c => c.Length == 6 && c.All(char.IsAsciiDigit));
        codes.Distinct().Count().ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Codes_cover_the_whole_range_including_leading_zeros()
    {
        PhoneVerificationRules.TryNormaliseCode("000123", out var code).ShouldBeTrue();
        code.ShouldBe("000123");
        PhoneVerificationRules.Hash("salt", "000123").ShouldNotBe(PhoneVerificationRules.Hash("salt", "123"));
    }

    [Fact]
    public void The_hash_depends_on_the_salt_and_never_contains_the_code()
    {
        var salt1 = PhoneVerificationRules.GenerateSalt();
        var salt2 = PhoneVerificationRules.GenerateSalt();

        salt1.ShouldNotBe(salt2);
        Convert.FromBase64String(salt1).Length.ShouldBe(PhoneVerificationRules.SaltBytes);
        var hash = PhoneVerificationRules.Hash(salt1, "482913");
        hash.ShouldNotBe(PhoneVerificationRules.Hash(salt2, "482913"));
        hash.ShouldNotContain("482913");
        Convert.FromBase64String(hash).Length.ShouldBe(32);
        hash.ShouldBe(PhoneVerificationRules.Hash(salt1, "482913"), "deterministic for the same salt and code");
    }

    [Theory]
    [InlineData("482913", "482913")]
    [InlineData(" 482 913 ", "482913")]
    public void A_typed_code_is_normalised(string typed, string expected)
    {
        PhoneVerificationRules.TryNormaliseCode(typed, out var code).ShouldBeTrue();
        code.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData("١٢٣٤٥٦")]
    public void Anything_but_six_ascii_digits_is_not_a_code(string? typed)
    {
        PhoneVerificationRules.TryNormaliseCode(typed, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_right_code_verifies_and_stamps_the_time()
    {
        var row = Row("482913");

        PhoneVerificationRules.Verify(row, "482913", Now.Plus(Duration.FromMinutes(3))).ShouldBe(PhoneVerificationOutcome.Verified);

        row.VerifiedAt.ShouldBe(Now.Plus(Duration.FromMinutes(3)));
        row.Attempts.ShouldBe(0);
    }

    [Fact]
    public void A_wrong_code_counts_an_attempt_and_the_fifth_kills_the_row()
    {
        var row = Row("482913");

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            PhoneVerificationRules.Verify(row, "000000", Now).ShouldBe(PhoneVerificationOutcome.WrongCode);
            row.Attempts.ShouldBe(attempt);
            PhoneVerificationRules.AttemptsLeft(row).ShouldBe(5 - attempt);
        }

        PhoneVerificationRules.Verify(row, "000000", Now).ShouldBe(PhoneVerificationOutcome.TooManyAttempts);
        row.Attempts.ShouldBe(5);
        PhoneVerificationRules.IsDead(row).ShouldBeTrue();
        PhoneVerificationRules.AttemptsLeft(row).ShouldBe(0);

        // The right code no longer helps, and the counter does not keep climbing.
        PhoneVerificationRules.Verify(row, "482913", Now).ShouldBe(PhoneVerificationOutcome.TooManyAttempts);
        row.Attempts.ShouldBe(5);
        row.VerifiedAt.ShouldBeNull();
    }

    [Fact]
    public void A_malformed_code_counts_as_a_wrong_one()
    {
        var row = Row("482913");

        PhoneVerificationRules.Verify(row, "48", Now).ShouldBe(PhoneVerificationOutcome.WrongCode);

        row.Attempts.ShouldBe(1);
    }

    [Fact]
    public void An_expired_row_refuses_even_the_right_code_without_counting()
    {
        var row = Row("482913");

        PhoneVerificationRules.Verify(row, "482913", row.ExpiresAt).ShouldBe(PhoneVerificationOutcome.Expired);

        row.Attempts.ShouldBe(0);
        row.VerifiedAt.ShouldBeNull();
        PhoneVerificationRules.IsExpired(row, row.ExpiresAt).ShouldBeTrue();
        PhoneVerificationRules.IsExpired(row, row.ExpiresAt.Minus(Duration.FromSeconds(1))).ShouldBeFalse();
    }

    [Fact]
    public void A_verified_row_is_spent()
    {
        var row = Row("482913");
        PhoneVerificationRules.Verify(row, "482913", Now).ShouldBe(PhoneVerificationOutcome.Verified);

        PhoneVerificationRules.Verify(row, "482913", Now).ShouldBe(PhoneVerificationOutcome.AlreadyVerified);
        PhoneVerificationRules.Verify(row, "000000", Now).ShouldBe(PhoneVerificationOutcome.AlreadyVerified);
        row.Attempts.ShouldBe(0);
    }

    [Fact]
    public void Lifetime_and_limits_are_the_agreed_values()
    {
        PhoneVerificationRules.Lifetime.ShouldBe(Duration.FromMinutes(10));
        PhoneVerificationRules.MaxAttempts.ShouldBe(5);
        PhoneVerificationRules.MaxCodesPerPhone.ShouldBe(3);
        PhoneVerificationRules.CodeRateWindow.ShouldBe(Duration.FromHours(1));
    }

    private static PhoneVerification Row(string code)
    {
        var salt = PhoneVerificationRules.GenerateSalt();
        return new PhoneVerification
        {
            PhoneE164 = "+447700900123",
            CodeSalt = salt,
            CodeHash = PhoneVerificationRules.Hash(salt, code),
            CreatedAt = Now,
            ExpiresAt = Now.Plus(PhoneVerificationRules.Lifetime),
            FirstName = "Olivia",
            LastName = "Brown",
        };
    }
}