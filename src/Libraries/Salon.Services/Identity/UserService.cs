using Microsoft.EntityFrameworkCore;

using NodaTime;

using Salon.Core.Identity;
using Salon.Data;

namespace Salon.Services.Identity;

internal sealed class UserService(SalonDbContext db, IPasswordService passwords, IClock clock) : IUserService
{
    /// <summary>A floor, not a policy: the owner picks the password; M2 adds strength rules and reset by email.</summary>
    public const int MinPasswordLength = 8;

    public async Task<User> CreateAsync(string email, string displayName, string password, Role role, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ValidatePassword(password);
        ValidateRole(role);

        var trimmedName = displayName.Trim();
        if (trimmedName.Length > User.DisplayNameMaxLength)
        {
            throw new ArgumentException($"Display name can have at most {User.DisplayNameMaxLength} characters.", nameof(displayName));
        }

        if (await db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"A user with email '{normalizedEmail}' already exists in this salon.");
        }

        var user = new User
        {
            Email = normalizedEmail,
            DisplayName = trimmedName,
            PasswordHash = string.Empty,
            Role = role,
            SecurityStamp = User.NewSecurityStamp(),
            CreatedAt = clock.GetCurrentInstant(),
        };
        user.PasswordHash = passwords.Hash(user, password);

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<User?>(null);
        }

        var normalized = email.Trim().ToLowerInvariant();
        return db.Users.FirstOrDefaultAsync(u => u.Email == normalized, cancellationToken);
    }

    public Task<User?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
        => db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

    public async Task SetPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        ValidatePassword(password);
        var user = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        user.PasswordHash = passwords.Hash(user, password);
        user.SecurityStamp = User.NewSecurityStamp();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetRoleAsync(Guid userId, Role role, CancellationToken cancellationToken = default)
    {
        ValidateRole(role);
        var user = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        user.Role = role;
        user.SecurityStamp = User.NewSecurityStamp();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeactivateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireAsync(userId, cancellationToken).ConfigureAwait(false);
        user.IsActive = false;
        user.SecurityStamp = User.NewSecurityStamp();
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        var normalized = email.Trim().ToLowerInvariant();
        var at = normalized.IndexOf('@', StringComparison.Ordinal);
        if (normalized.Length > User.EmailMaxLength || at <= 0 || at == normalized.Length - 1 || normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException($"Email must look like name@domain and have at most {User.EmailMaxLength} characters.", nameof(email));
        }

        return normalized;
    }

    private static void ValidatePassword(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        if (password.Length < MinPasswordLength)
        {
            throw new ArgumentException($"Password must have at least {MinPasswordLength} characters.", nameof(password));
        }
    }

    private static void ValidateRole(Role role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");
        }
    }

    private async Task<User> RequireAsync(Guid userId, CancellationToken cancellationToken)
        => await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException($"User {userId} does not exist in the current tenant.");
}