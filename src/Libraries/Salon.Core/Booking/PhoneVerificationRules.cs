using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using NodaTime;

namespace Salon.Core.Booking;

/// <summary>
/// The pure rules of an SMS verification code (ADR 0014): six digits from a cryptographic generator, a per-row salt, a
/// SHA-256 hash that is compared in constant time, ten minutes of life, five wrong attempts. No clock, no database: callers
/// pass <c>now</c>. The code itself exists only in the caller's memory and in the SMS.
/// </summary>
public static class PhoneVerificationRules
{
    public const int CodeLength = 6;
    public const int MaxAttempts = 5;
    public const int SaltBytes = 16;

    /// <summary>At most this many codes per phone number inside <see cref="CodeRateWindow"/>.</summary>
    public const int MaxCodesPerPhone = 3;

    private const int CodeSpace = 1_000_000;

    public static Duration Lifetime { get; } = Duration.FromMinutes(10);

    public static Duration CodeRateWindow { get; } = Duration.FromHours(1);

    /// <summary>A uniformly random code "000000"–"999999".</summary>
    public static string GenerateCode()
        => RandomNumberGenerator.GetInt32(CodeSpace).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>Base64 of <see cref="SaltBytes"/> random bytes.</summary>
    public static string GenerateSalt()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltBytes));

    /// <summary>Base64 SHA-256 of <c>"{salt}:{code}"</c>.</summary>
    public static string Hash(string salt, string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(salt);
        ArgumentException.ThrowIfNullOrEmpty(code);
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{code}")));
    }

    /// <summary>Exactly <see cref="CodeLength"/> ASCII digits; spaces around and between groups are tolerated.</summary>
    public static bool TryNormaliseCode(string? raw, out string code)
    {
        code = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var compact = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length != CodeLength || !compact.All(char.IsAsciiDigit))
        {
            return false;
        }

        code = compact;
        return true;
    }

    public static bool IsDead(PhoneVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return verification.Attempts >= MaxAttempts;
    }

    public static bool IsExpired(PhoneVerification verification, Instant now)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return now >= verification.ExpiresAt;
    }

    public static int AttemptsLeft(PhoneVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        return Math.Max(0, MaxAttempts - verification.Attempts);
    }

    /// <summary>
    /// Checks <paramref name="code"/> against the row. A wrong code counts an attempt (the caller persists the row); the
    /// right one stamps <see cref="PhoneVerification.VerifiedAt"/>. A spent, dead or expired row never verifies, whatever
    /// the code, and counts nothing.
    /// </summary>
    public static PhoneVerificationOutcome Verify(PhoneVerification verification, string? code, Instant now)
    {
        ArgumentNullException.ThrowIfNull(verification);

        if (verification.VerifiedAt is not null)
        {
            return PhoneVerificationOutcome.AlreadyVerified;
        }

        if (IsDead(verification))
        {
            return PhoneVerificationOutcome.TooManyAttempts;
        }

        if (IsExpired(verification, now))
        {
            return PhoneVerificationOutcome.Expired;
        }

        if (!TryNormaliseCode(code, out var normalised))
        {
            verification.Attempts++;
            return IsDead(verification) ? PhoneVerificationOutcome.TooManyAttempts : PhoneVerificationOutcome.WrongCode;
        }

        var expected = Convert.FromBase64String(verification.CodeHash);
        var actual = Convert.FromBase64String(Hash(verification.CodeSalt, normalised));
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
        {
            verification.Attempts++;
            return IsDead(verification) ? PhoneVerificationOutcome.TooManyAttempts : PhoneVerificationOutcome.WrongCode;
        }

        verification.VerifiedAt = now;
        return PhoneVerificationOutcome.Verified;
    }
}

public enum PhoneVerificationOutcome
{
    Verified = 1,
    WrongCode = 2,
    Expired = 3,
    TooManyAttempts = 4,
    AlreadyVerified = 5,
}
