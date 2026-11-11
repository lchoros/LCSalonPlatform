using Microsoft.Extensions.Caching.Memory;

using Salon.Services.Caching;

namespace Salon.Tests.Unit.Caching;

public sealed class MemoryCacheManagerTests : IDisposable
{
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly MemoryCacheManager _cache;

    public MemoryCacheManagerTests()
    {
        _cache = new MemoryCacheManager(_memoryCache);
    }

    public void Dispose() => _memoryCache.Dispose();

    [Fact]
    public void Create_sets_key_prefixes_and_a_default_cache_time_of_60_minutes()
    {
        var key = CacheKey.Create("Salon.tenant.slug.studio-20", "Salon.tenant.");

        key.Key.ShouldBe("Salon.tenant.slug.studio-20");
        key.Prefixes.ShouldBe(["Salon.tenant."]);
        key.CacheTime.ShouldBe(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task Get_acquires_once_and_serves_the_cached_value_afterwards()
    {
        var key = CacheKey.Create("Salon.test.counter");
        var calls = 0;

        var first = await _cache.GetAsync(key, () => Task.FromResult(++calls));
        var second = await _cache.GetAsync(key, () => Task.FromResult(++calls));

        first.ShouldBe(1);
        second.ShouldBe(1);
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task An_acquired_null_is_cached_too()
    {
        var key = CacheKey.Create("Salon.test.null");
        var calls = 0;

        await _cache.GetAsync<string?>(key, () =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });
        var again = await _cache.GetAsync<string?>(key, () =>
        {
            calls++;
            return Task.FromResult<string?>("late");
        });

        again.ShouldBeNull();
        calls.ShouldBe(1);
    }

    [Fact]
    public async Task Set_then_get_returns_the_value_without_acquiring()
    {
        var key = CacheKey.Create("Salon.test.set");
        await _cache.SetAsync(key, "stored");

        var value = await _cache.GetAsync<string>(key, () => throw new InvalidOperationException("should not acquire"));

        value.ShouldBe("stored");
    }

    [Fact]
    public async Task Remove_drops_only_that_key()
    {
        var one = CacheKey.Create("Salon.test.one");
        var two = CacheKey.Create("Salon.test.two");
        await _cache.SetAsync(one, 1);
        await _cache.SetAsync(two, 2);

        await _cache.RemoveAsync(one);

        (await _cache.GetAsync(one, () => Task.FromResult(-1))).ShouldBe(-1);
        (await _cache.GetAsync(two, () => Task.FromResult(-1))).ShouldBe(2);
    }

    [Fact]
    public async Task Remove_by_prefix_removes_only_keys_registered_under_that_prefix()
    {
        var tenantA = CacheKey.Create("Salon.tenant.slug.a", "Salon.tenant.");
        var tenantB = CacheKey.Create("Salon.tenant.id.b", "Salon.tenant.");
        var settings = CacheKey.Create("Salon.settings.x", "Salon.settings.");
        await _cache.SetAsync(tenantA, "a");
        await _cache.SetAsync(tenantB, "b");
        await _cache.SetAsync(settings, "s");

        await _cache.RemoveByPrefixAsync("Salon.tenant.");

        (await _cache.GetAsync(tenantA, () => Task.FromResult("miss"))).ShouldBe("miss");
        (await _cache.GetAsync(tenantB, () => Task.FromResult("miss"))).ShouldBe("miss");
        (await _cache.GetAsync(settings, () => Task.FromResult("miss"))).ShouldBe("s");
    }

    [Fact]
    public async Task Remove_by_prefix_with_no_matching_keys_is_a_no_op()
    {
        var key = CacheKey.Create("Salon.settings.x", "Salon.settings.");
        await _cache.SetAsync(key, "s");

        await _cache.RemoveByPrefixAsync("Salon.nothing.");

        (await _cache.GetAsync(key, () => Task.FromResult("miss"))).ShouldBe("s");
    }

    [Fact]
    public async Task Clear_empties_everything()
    {
        var one = CacheKey.Create("Salon.a.one", "Salon.a.");
        var two = CacheKey.Create("Salon.b.two", "Salon.b.");
        await _cache.SetAsync(one, 1);
        await _cache.SetAsync(two, 2);

        await _cache.ClearAsync();

        (await _cache.GetAsync(one, () => Task.FromResult(-1))).ShouldBe(-1);
        (await _cache.GetAsync(two, () => Task.FromResult(-1))).ShouldBe(-1);
    }

    [Fact]
    public async Task Concurrent_sets_and_prefix_removals_do_not_corrupt_the_key_index()
    {
        var tasks = Enumerable.Range(0, 200).Select(async i =>
        {
            await _cache.SetAsync(CacheKey.Create($"Salon.load.{i}", "Salon.load."), i);
            if (i % 10 == 0)
            {
                await _cache.RemoveByPrefixAsync("Salon.load.");
            }
        });

        await Task.WhenAll(tasks);
        await _cache.RemoveByPrefixAsync("Salon.load.");

        for (var i = 0; i < 200; i++)
        {
            (await _cache.GetAsync(CacheKey.Create($"Salon.load.{i}"), () => Task.FromResult(-1))).ShouldBe(-1);
        }
    }
}
public class MemoryCacheManagerEvictionTests
{
    [Fact]
    public async Task A_value_set_after_its_predecessor_expired_stays_invalidatable_by_prefix()
    {
        using var memory = new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions());
        var manager = new MemoryCacheManager(memory);
        // Same key string and the same prefix list instance (as `with` copies share), as static readonly keys do.
        var key = CacheKey.Create("Salon.evict.item", "Salon.evict.");
        var shortLived = key with { CacheTime = TimeSpan.FromMilliseconds(1) };

        await manager.SetAsync(shortLived, "old");
        await Task.Delay(30);
        (await manager.GetAsync(key, () => Task.FromResult("new"))).ShouldBe("new");
        // The expired entry's eviction callback runs on the thread pool shortly after it was detected.
        await Task.Delay(300);

        await manager.RemoveByPrefixAsync("Salon.evict.");

        memory.TryGetValue("Salon.evict.item", out _).ShouldBeFalse("the fresh entry fell out of the prefix index");
    }
}