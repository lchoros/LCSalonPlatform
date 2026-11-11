using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;

using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Caching;

namespace Salon.Services.Tenancy;

/// <summary>Tenant rows change rarely; snapshots are cached under <see cref="TenantCacheKeys"/> (unknown slugs included).</summary>
internal sealed class TenantLookup(SalonDbContext db, IStaticCacheManager cache) : ITenantLookup
{
    private static readonly Expression<Func<Tenant, TenantInfo>> ToInfo =
        t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId, t.Culture, t.Currency, t.IsActive);

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return cache.GetAsync(TenantCacheKeys.BySlug(slug), () =>
            db.Tenants.AsNoTracking().Where(t => t.Slug == slug).Select(ToInfo).FirstOrDefaultAsync(cancellationToken));
    }

    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => cache.GetAsync(TenantCacheKeys.ById(tenantId), () =>
            db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(ToInfo).FirstOrDefaultAsync(cancellationToken));
}