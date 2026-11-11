using Microsoft.Extensions.Logging;

using NodaTime;

using Salon.Core.Identity;
using Salon.Data;

namespace Salon.Services.Identity;

internal sealed class SignInService(SalonDbContext db, IUserService users, IPasswordService passwords, IClock clock, ILogger<SignInService> logger) : ISignInService
{
    private static readonly User Decoy = new()
    {
        Email = "decoy@salon.invalid",
        DisplayName = "decoy",
        PasswordHash = string.Empty,
        SecurityStamp = string.Empty,
    };

    // Hashed once per process; a sign-in for an unknown email still pays for one hash so it takes as long as a wrong password.
    private static string? _decoyHash;

    public async Task<SignInResult> AttemptAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            return SignInResult.Failed;
        }

        var user = await users.FindByEmailAsync(email, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            _decoyHash ??= passwords.Hash(Decoy, Guid.NewGuid().ToString("N"));
            passwords.Verify(Decoy, _decoyHash, password);
            return SignInResult.Failed;
        }

        var now = clock.GetCurrentInstant();
        if (LockoutPolicy.IsLockedOut(user, now))
        {
            return SignInResult.Locked;
        }

        var verification = passwords.Verify(user, user.PasswordHash, password);
        if (!verification.Succeeded || !user.IsActive)
        {
            var locked = LockoutPolicy.RecordFailure(user, now);
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (locked)
            {
                logger.LogWarning("User {UserId} locked out after {Count} failed sign-ins", user.Id, LockoutPolicy.MaxFailedAttempts);
                return SignInResult.Locked;
            }

            return SignInResult.Failed;
        }

        LockoutPolicy.RecordSuccess(user, now);
        if (verification.NeedsRehash)
        {
            user.PasswordHash = passwords.Hash(user, password);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SignInResult.Success(user);
    }
}