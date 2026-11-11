namespace Salon.Services.Caching;

/// <summary>
/// A cache entry's key plus the prefixes it can be invalidated by (e.g. "Salon.tenant.slug.studio-20" under "Salon.tenant.").
/// Keys follow "Salon.{area}.{what}.{id}" so prefixes stay readable; <see cref="CacheTime"/> defaults to 60 minutes.
/// </summary>
public sealed record CacheKey
{
    public static readonly TimeSpan DefaultCacheTime = TimeSpan.FromMinutes(60);

    public CacheKey(string key, IReadOnlyList<string> prefixes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(prefixes);
        Key = key;
        Prefixes = prefixes;
    }

    public string Key { get; }

    public IReadOnlyList<string> Prefixes { get; }

    public TimeSpan CacheTime { get; init; } = DefaultCacheTime;

    public static CacheKey Create(string key, params string[] prefixes) => new(key, prefixes);
}