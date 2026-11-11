using Salon.Core.Identity;

namespace Salon.Services.Identity;

/// <summary>
/// Hashes and checks passwords. The implementation (PBKDF2 through the framework's <c>PasswordHasher&lt;User&gt;</c>, ADR
/// 0009) lives in Salon.Web.Framework because that type ships with ASP.NET Core, which the service layer does not reference.
/// </summary>
public interface IPasswordService
{
    string Hash(User user, string password);

    PasswordVerification Verify(User user, string passwordHash, string password);
}

/// <summary>Outcome of a password check. <see cref="NeedsRehash"/> is set when the stored hash uses older parameters.</summary>
public readonly record struct PasswordVerification(bool Succeeded, bool NeedsRehash)
{
    public static PasswordVerification Failed => new(false, false);

    public static PasswordVerification Success => new(true, false);

    public static PasswordVerification SuccessRehashNeeded => new(true, true);
}