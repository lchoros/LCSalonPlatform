using NodaTime;
using Salon.Core.Booking;

namespace Salon.Tests.Unit.Booking;

/// <summary>The retention cut-off of ADR 0017: a day by default, never shorter than a code's life, strictly older rows only.</summary>
public class PhoneVerificationRetentionTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 12, 10, 0);

    [Fact]
    public void The_default_retention_is_a_day()
    {
        PhoneVerificationRules.DefaultRetention.ShouldBe(Duration.FromHours(24));
        PhoneVerificationRules.RetentionCutOff(Now, PhoneVerificationRules.DefaultRetention).ShouldBe(Now - Duration.FromHours(24));
    }

    [Fact]
    public void A_retention_shorter_than_the_code_lifetime_is_refused()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => PhoneVerificationRules.RetentionCutOff(Now, Duration.FromMinutes(9)));
        PhoneVerificationRules.RetentionCutOff(Now, PhoneVerificationRules.Lifetime).ShouldBe(Now - PhoneVerificationRules.Lifetime);
    }

    [Theory]
    [InlineData(25, true)]
    [InlineData(24, false)]
    [InlineData(1, false)]
    public void Only_rows_created_strictly_before_the_cut_off_are_past_retention(int hoursAgo, bool expected)
    {
        var verification = Row(Now - Duration.FromHours(hoursAgo));

        PhoneVerificationRules.IsPastRetention(verification, Now, Duration.FromHours(24)).ShouldBe(expected);
    }

    [Fact]
    public void A_verified_row_is_kept_until_the_cut_off_like_any_other()
    {
        var verified = Row(Now - Duration.FromHours(2));
        verified.VerifiedAt = Now - Duration.FromHours(2) + Duration.FromMinutes(1);

        PhoneVerificationRules.IsPastRetention(verified, Now, Duration.FromHours(24)).ShouldBeFalse();
        PhoneVerificationRules.IsPastRetention(verified, Now + Duration.FromHours(23), Duration.FromHours(24)).ShouldBeTrue();
    }

    private static PhoneVerification Row(Instant createdAt) => new()
    {
        PhoneE164 = "+447700900123",
        CodeSalt = "salt",
        CodeHash = PhoneVerificationRules.Hash("salt", "123456"),
        CreatedAt = createdAt,
        ExpiresAt = createdAt + PhoneVerificationRules.Lifetime,
        FirstName = "Olivia",
        LastName = "Brown",
    };
}