using Salon.Services.Caching;

namespace Salon.Services.Tenancy;

/// <summary>Cache keys for tenant snapshots. Whoever edits a tenant calls RemoveByPrefixAsync(<see cref="Prefix"/>).</summary>
public static class TenantCacheKeys
{
    public const string Prefix = "Salon.tenant.";

    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(5);

    public static CacheKey BySlug(string slug) => CacheKey.Create($"{Prefix}slug.{slug}", Prefix) with { CacheTime = CacheTime };

    public static CacheKey ById(Guid tenantId) => CacheKey.Create($"{Prefix}id.{tenantId}", Prefix) with { CacheTime = CacheTime };
}