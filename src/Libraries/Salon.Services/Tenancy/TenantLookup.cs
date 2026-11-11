using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Services.Tenancy;

/// <summary>
/// Tenant rows change rarely; hits are cached for a few minutes. Misses are never cached: the slug comes from the Host
/// header, so caching unknown slugs would let anyone grow the cache without bound. Slugs are stored and compared lowercase.
/// Will move to IStaticCacheManager when it lands.
/// </summary>
internal sealed class TenantLookup(SalonDbContext db, IMemoryCache cache) : ITenantLookup
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static readonly Expression<Func<Tenant, TenantInfo>> ToInfo =
        t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId, t.Culture, t.Currency, t.IsActive);

    public async Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var normalized = slug.ToLowerInvariant();
        var key = $"tenant:slug:{normalized}";
        if (cache.TryGetValue(key, out TenantInfo? hit))
        {
            return hit;
        }

        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Slug == normalized).Select(ToInfo).FirstOrDefaultAsync(cancellationToken);
        if (tenant is not null)
        {
            cache.Set(key, tenant, Ttl);
        }

        return tenant;
    }

    public async Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var key = $"tenant:id:{tenantId}";
        if (cache.TryGetValue(key, out TenantInfo? hit))
        {
            return hit;
        }

        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(ToInfo).FirstOrDefaultAsync(cancellationToken);
        if (tenant is not null)
        {
            cache.Set(key, tenant, Ttl);
        }

        return tenant;
    }
}