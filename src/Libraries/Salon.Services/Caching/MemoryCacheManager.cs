using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace Salon.Services.Caching;

/// <summary>
/// <see cref="IStaticCacheManager"/> on <see cref="IMemoryCache"/>. IMemoryCache cannot enumerate its keys, so the
/// manager keeps its own index of key → prefixes; eviction callbacks keep the index from growing past the cache.
/// </summary>
public sealed class MemoryCacheManager(IMemoryCache cache) : IStaticCacheManager
{
    private readonly ConcurrentDictionary<string, Tracked> _index = new(StringComparer.Ordinal);

    public async Task<T> GetAsync<T>(CacheKey key, Func<Task<T>> acquire)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(acquire);

        if (cache.TryGetValue(key.Key, out T? cached))
        {
            return cached!;
        }

        var value = await acquire().ConfigureAwait(false);
        Set(key, value);
        return value;
    }

    public Task SetAsync<T>(CacheKey key, T value)
    {
        ArgumentNullException.ThrowIfNull(key);
        Set(key, value);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CacheKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Remove(key.Key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        foreach (var (key, tracked) in _index)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal) || tracked.Prefixes.Contains(prefix, StringComparer.Ordinal))
            {
                Remove(key);
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        foreach (var key in _index.Keys)
        {
            Remove(key);
        }

        return Task.CompletedTask;
    }

    private static void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        // Replaced means a newer Set already owns the index slot; otherwise drop exactly the entry that expired.
        if (reason != EvictionReason.Replaced && state is Tracked tracked)
        {
            tracked.Owner._index.TryRemove(new KeyValuePair<string, Tracked>((string)key, tracked));
        }
    }

    private void Set<T>(CacheKey key, T value)
    {
        var tracked = new Tracked(this, key.Prefixes);
        using var entry = cache.CreateEntry(key.Key);
        entry.Value = value;
        entry.AbsoluteExpirationRelativeToNow = key.CacheTime;
        entry.RegisterPostEvictionCallback(OnEvicted, tracked);
        _index[key.Key] = tracked;
    }

    private void Remove(string key)
    {
        _index.TryRemove(key, out _);
        cache.Remove(key);
    }

    /// <summary>A class, not a record: the eviction callback must only remove the exact entry it was registered for (reference equality).</summary>
    private sealed class Tracked(MemoryCacheManager owner, IReadOnlyList<string> prefixes)
    {
        public MemoryCacheManager Owner { get; } = owner;

        public IReadOnlyList<string> Prefixes { get; } = prefixes;
    }
}