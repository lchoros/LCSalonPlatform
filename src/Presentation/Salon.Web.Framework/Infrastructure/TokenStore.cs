using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Salon.Web.Framework.Infrastructure;

/// <summary>
/// Something a person checks on one page and confirms on the next (ADR 0045, after the client import of ADR 0019):
/// kept in the host's memory cache for fifteen minutes under a random 256-bit token. The browser only ever posts the
/// token back, so what is confirmed is what was shown and cannot be tampered with. The value should carry its tenant;
/// the caller checks it on every use.
/// </summary>
/// <param name="keyPrefix">Keeps the tokens of one use apart from those of another, e.g. "product-import:".</param>
public sealed class TokenStore<T>(IMemoryCache cache, string keyPrefix)
    where T : class
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    /// <summary>Length of a token: 64 lower-case hex characters.</summary>
    public const int TokenLength = 64;

    /// <summary>Stores a fresh value and returns its token.</summary>
    public string Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(TokenLength / 2));
        Replace(token, value);
        return token;
    }

    /// <summary>Overwrites the value under an existing token; the fifteen minutes start again.</summary>
    public void Replace(string token, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(value);
        cache.Set(keyPrefix + token, value, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
    }

    public T? Find(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return cache.TryGetValue(keyPrefix + token, out T? value) ? value : null;
    }
}