using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Salon.Core.Identity;
using Salon.Services.Identity;

namespace Salon.Web.Framework.Identity;

/// <summary>
/// <see cref="IPasswordService"/> over the framework's <see cref="PasswordHasher{TUser}"/> (ADR 0009): PBKDF2-HMAC-SHA512
/// with a random salt and, in .NET 10, 100 000 iterations. The hasher is stateless, so one instance serves the host.
/// </summary>
internal sealed class PasswordHasherPasswordService(IOptions<PasswordHasherOptions> options) : IPasswordService
{
    private readonly PasswordHasher<User> _hasher = new(options);

    public string Hash(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(user, password);
    }

    public PasswordVerification Verify(User user, string passwordHash, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerification.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(user, passwordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerification.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
                _ => PasswordVerification.Failed,
            };
        }
        catch (FormatException)
        {
            // A stored value that is not a hash at all (corrupt row, manual edit) is a failed check, not a server error.
            return PasswordVerification.Failed;
        }
    }
}