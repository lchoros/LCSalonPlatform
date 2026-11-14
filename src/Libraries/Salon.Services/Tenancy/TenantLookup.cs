using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Caching;

namespace Salon.Services.Tenancy;

/// <summary>
/// Tenant rows change rarely; hits are cached under <see cref="TenantCacheKeys"/>. Misses are never kept: the slug comes
/// from the Host header, so caching unknown slugs would let anyone grow the cache without bound. Slugs are stored and
/// compared lowercase.
/// </summary>
internal sealed class TenantLookup(SalonDbContext db, IStaticCacheManager cache) : ITenantLookup
{
    private static readonly Expression<Func<Tenant, TenantInfo>> ToInfo =
        t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId, t.Culture, t.Currency, t.IsActive);

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var normalized = slug.ToLowerInvariant();
        return GetHitOrForgetAsync(TenantCacheKeys.BySlug(normalized), () =>
            db.Tenants.AsNoTracking().Where(t => t.Slug == normalized).Select(ToInfo).FirstOrDefaultAsync(cancellationToken));
    }

    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => GetHitOrForgetAsync(TenantCacheKeys.ById(tenantId), () =>
            db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(ToInfo).FirstOrDefaultAsync(cancellationToken));

    private async Task<TenantInfo?> GetHitOrForgetAsync(CacheKey key, Func<Task<TenantInfo?>> acquire)
    {
        var tenant = await cache.GetAsync(key, acquire).ConfigureAwait(false);
        if (tenant is null)
        {
            await cache.RemoveAsync(key).ConfigureAwait(false);
        }

        return tenant;
    }
}