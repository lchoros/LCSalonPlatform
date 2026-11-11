using Microsoft.EntityFrameworkCore;

using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Caching;

namespace Salon.Services.Settings;

/// <summary>
/// Reads all of the current tenant's rows once into the cache ("Salon.settings.{tenantId}") and binds typed settings
/// from that dictionary; every write upserts rows and drops the tenant's entry. The EF tenant filter and RLS scope the
/// queries, and the cache key carries the tenant id, so one tenant can never see another's settings.
/// </summary>
internal sealed class SettingService(SalonDbContext db, ITenantContext tenantContext, IStaticCacheManager cache) : ISettingService
{
    public const string CachePrefix = "Salon.settings.";

    public async Task<TSettings> LoadAsync<TSettings>(CancellationToken cancellationToken = default)
        where TSettings : ISettings, new()
    {
        var rows = await GetAllAsync(RequireTenant(), cancellationToken).ConfigureAwait(false);
        return SettingsBinder.Bind<TSettings>(rows);
    }

    public Task SaveAsync<TSettings>(TSettings settings, CancellationToken cancellationToken = default)
        where TSettings : ISettings
    {
        ArgumentNullException.ThrowIfNull(settings);
        return UpsertAsync(RequireTenant(), SettingsBinder.Flatten(settings).ToList(), cancellationToken);
    }

    public async Task<string?> GetRawAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var rows = await GetAllAsync(RequireTenant(), cancellationToken).ConfigureAwait(false);
        return rows.GetValueOrDefault(name);
    }

    public Task SetRawAsync(string name, string? value, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return UpsertAsync(RequireTenant(), [KeyValuePair.Create(name, value)], cancellationToken);
    }

    internal static CacheKey KeyFor(Guid tenantId) => CacheKey.Create($"{CachePrefix}{tenantId}", CachePrefix);

    private Guid RequireTenant()
        => tenantContext.TenantId
           ?? throw new TenantViolationException("Settings are per tenant, but no tenant is set in the current scope.");

    private Task<IReadOnlyDictionary<string, string?>> GetAllAsync(Guid tenantId, CancellationToken cancellationToken)
        => cache.GetAsync<IReadOnlyDictionary<string, string?>>(KeyFor(tenantId), async () =>
            await db.Settings.AsNoTracking()
                .ToDictionaryAsync(s => s.Name, s => s.Value, StringComparer.Ordinal, cancellationToken)
                .ConfigureAwait(false));

    private async Task UpsertAsync(Guid tenantId, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancellationToken)
    {
        var names = values.Select(v => v.Key).ToList();
        var existing = await db.Settings
            .Where(s => names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        foreach (var (name, value) in values)
        {
            if (existing.TryGetValue(name, out var row))
            {
                row.Value = value;
            }
            else
            {
                db.Settings.Add(new Setting { TenantId = tenantId, Name = name, Value = value });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await cache.RemoveAsync(KeyFor(tenantId)).ConfigureAwait(false);
    }
}