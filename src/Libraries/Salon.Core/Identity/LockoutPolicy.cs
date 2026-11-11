using NodaTime;

namespace Salon.Core.Identity;

/// <summary>
/// The sign-in counting rules of ADR 0009, kept free of persistence so they are testable with a fixed clock:
/// five consecutive failures lock the account for fifteen minutes, a success clears the count, and a locked account
/// is never even password-checked.
/// </summary>
public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 5;

    public static Duration LockoutDuration { get; } = Duration.FromMinutes(15);

    public static bool IsLockedOut(User user, Instant now)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.LockedOutUntil is { } until && until > now;
    }

    /// <summary>Counts one failure; returns true when this failure locked the account.</summary>
    public static bool RecordFailure(User user, Instant now)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.FailedLoginCount++;
        if (user.FailedLoginCount < MaxFailedAttempts)
        {
            return false;
        }

        user.FailedLoginCount = 0;
        user.LockedOutUntil = now + LockoutDuration;
        return true;
    }

    public static void RecordSuccess(User user, Instant now)
    {
        ArgumentNullException.ThrowIfNull(user);
        user.FailedLoginCount = 0;
        user.LockedOutUntil = null;
        user.LastLoginAt = now;
    }
}