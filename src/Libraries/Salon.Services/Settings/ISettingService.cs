using Salon.Core.Settings;

namespace Salon.Services.Settings;

/// <summary>
/// Typed, per-tenant settings (CLAUDE.md rule 7). Values are stored one row per property and cached per tenant.
/// Every member throws <see cref="Salon.Core.Tenancy.TenantViolationException"/> when no tenant is in scope,
/// because settings without a tenant would otherwise silently read as defaults.
/// </summary>
public interface ISettingService
{
    /// <summary>Binds the tenant's rows onto a new <typeparamref name="TSettings"/>; properties without a row keep their defaults.</summary>
    Task<TSettings> LoadAsync<TSettings>(CancellationToken cancellationToken = default)
        where TSettings : ISettings, new();

    /// <summary>Upserts one row per public writable property and invalidates the tenant's settings cache.</summary>
    Task SaveAsync<TSettings>(TSettings settings, CancellationToken cancellationToken = default)
        where TSettings : ISettings;

    Task<string?> GetRawAsync(string name, CancellationToken cancellationToken = default);

    Task SetRawAsync(string name, string? value, CancellationToken cancellationToken = default);
}