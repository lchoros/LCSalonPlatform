using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Services.Tenancy;

/// <summary>Tenant rows change rarely; cache them for a few minutes. Will move to IStaticCacheManager when it lands.</summary>
internal sealed class TenantLookup(SalonDbContext db, IMemoryCache cache) : ITenantLookup
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private static readonly Expression<Func<Tenant, TenantInfo>> ToInfo =
        t => new TenantInfo(t.Id, t.Slug, t.Name, t.TimeZoneId, t.Culture, t.Currency, t.IsActive);

    public Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        return cache.GetOrCreateAsync($"tenant:slug:{slug.ToUpperInvariant()}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return db.Tenants.AsNoTracking().Where(t => t.Slug == slug).Select(ToInfo).FirstOrDefaultAsync(cancellationToken);
        });
    }

    public Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => cache.GetOrCreateAsync($"tenant:id:{tenantId}", entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return db.Tenants.AsNoTracking().Where(t => t.Id == tenantId).Select(ToInfo).FirstOrDefaultAsync(cancellationToken);
        });
}