using Salon.Core.Identity;

namespace Salon.Services.Identity;

/// <summary>Checks credentials against the current tenant's users and applies <see cref="LockoutPolicy"/> (ADR 0009).</summary>
public interface ISignInService
{
    /// <summary>
    /// An unknown email, a wrong password and an inactive account all yield the same failure; only a lockout is told
    /// apart, and a locked account is not password-checked at all.
    /// </summary>
    Task<SignInResult> AttemptAsync(string email, string password, CancellationToken cancellationToken = default);
}

/// <param name="Succeeded">Credentials matched an active, unlocked user.</param>
/// <param name="LockedOut">The account is locked (whether this attempt caused it or found it so).</param>
/// <param name="User">The signed-in user when <paramref name="Succeeded"/>, otherwise null.</param>
public sealed record SignInResult(bool Succeeded, bool LockedOut, User? User)
{
    public static SignInResult Failed { get; } = new(false, false, null);

    public static SignInResult Locked { get; } = new(false, true, null);

    public static SignInResult Success(User user) => new(true, false, user);
}