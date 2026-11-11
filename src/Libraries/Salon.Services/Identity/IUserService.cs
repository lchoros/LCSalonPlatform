using Salon.Core.Identity;

namespace Salon.Services.Identity;

/// <summary>
/// Manages the current tenant's users (ADR 0009). Every change that affects an existing session (password, role,
/// deactivation) rotates the security stamp, so the cookie-validation hook ends those sessions.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Creates a user in the current tenant. The email is trimmed and lower-cased; a duplicate within the tenant throws
    /// <see cref="InvalidOperationException"/>, an invalid argument <see cref="ArgumentException"/>.
    /// </summary>
    Task<User> CreateAsync(string email, string displayName, string password, Role role, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive lookup within the current tenant; null when there is no such user.</summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default);

    Task SetRoleAsync(Guid userId, Role role, CancellationToken cancellationToken = default);

    Task DeactivateAsync(Guid userId, CancellationToken cancellationToken = default);
}