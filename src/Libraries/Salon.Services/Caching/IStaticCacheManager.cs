namespace Salon.Services.Caching;

/// <summary>
/// Process-wide cache for rarely changing data (tenants, settings, localization). Keys carry prefixes so a whole
/// area can be invalidated at once. In-memory until M3; a distributed implementation drops in behind this interface.
/// Entries live above the tenant filter, so every per-tenant key must include the tenant id.
/// </summary>
public interface IStaticCacheManager
{
    /// <summary>Returns the cached value or runs <paramref name="acquire"/> once and caches its result (null included).</summary>
    Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire);

    Task SetAsync<T>(CacheKey key, T value);

    Task RemoveAsync(CacheKey key);

    /// <summary>Removes every entry registered under <paramref name="prefix"/> or whose key starts with it.</summary>
    Task RemoveByPrefixAsync(string prefix);

    Task ClearAsync();
}