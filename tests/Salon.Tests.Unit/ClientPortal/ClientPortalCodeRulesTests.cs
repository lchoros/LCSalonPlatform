using NodaTime;
using Salon.Core.Booking;

namespace Salon.Tests.Unit.ClientPortal;

/// <summary>
/// What ADR 0083 adds to the code rules of ADR 0014: a row for a number the salon does not know can never be proven,
/// and a row that only proves a number is never read as a booking.
/// </summary>
public class ClientPortalCodeRulesTests
{
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 20, 10, 0);

    [Fact]
    public void The_stand_in_for_a_code_is_never_six_digits_and_differs_every_time()
    {
        var secrets = Enumerable.Range(0, 500).Select(_ => PhoneVerificationRules.GenerateUnmatchableSecret()).ToList();

        secrets.Where(s => PhoneVerificationRules.TryNormaliseCode(s, out _)).ShouldBeEmpty();
        secrets.Distinct(StringComparer.Ordinal).Count().ShouldBe(secrets.Count);
    }

    [Fact]
    public void A_row_hashed_from_the_stand_in_refuses_every_code_and_dies_like_any_other()
    {
        var secret = PhoneVerificationRules.GenerateUnmatchableSecret();
        var row = Row(secret);

        // Even the stand-in itself is refused: what is typed must be six digits before it is compared.
        PhoneVerificationRules.Verify(row, secret, Now).ShouldBe(PhoneVerificationOutcome.WrongCode);
        PhoneVerificationRules.Verify(row, "000000", Now).ShouldBe(PhoneVerificationOutcome.WrongCode);
        PhoneVerificationRules.Verify(row, "123456", Now).ShouldBe(PhoneVerificationOutcome.WrongCode);
        PhoneVerificationRules.Verify(row, "999999", Now).ShouldBe(PhoneVerificationOutcome.WrongCode);
        PhoneVerificationRules.Verify(row, "482913", Now).ShouldBe(PhoneVerificationOutcome.TooManyAttempts);

        row.Attempts.ShouldBe(PhoneVerificationRules.MaxAttempts);
        row.VerifiedAt.ShouldBeNull();
    }

    [Fact]
    public void A_row_that_only_proves_a_number_has_no_service_to_book()
    {
        var signIn = Row("482913");
        var booking = Row("482913");
        booking.Purpose = PhoneVerificationPurpose.Booking;
        booking.ServiceId = Guid.NewGuid();

        PhoneVerificationRules.ServiceOf(booking).ShouldBe(booking.ServiceId.Value);
        Should.Throw<InvalidOperationException>(() => PhoneVerificationRules.ServiceOf(signIn));
    }

    private static PhoneVerification Row(string secret)
    {
        var salt = PhoneVerificationRules.GenerateSalt();
        return new PhoneVerification
        {
            Purpose = PhoneVerificationPurpose.ClientPortal,
            PhoneE164 = "+447700900123",
            CodeSalt = salt,
            CodeHash = PhoneVerificationRules.Hash(salt, secret),
            FirstName = string.Empty,
            LastName = string.Empty,
            CreatedAt = Now,
            ExpiresAt = Now + PhoneVerificationRules.Lifetime,
        };
    }
}