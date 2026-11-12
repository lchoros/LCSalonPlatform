using System.Security.Cryptography;

using Microsoft.Extensions.Caching.Memory;

using Salon.Services.Clients.Import;

namespace Salon.Web.Features.Clients.ImportClients;

/// <summary>
/// The parsed preview between step 2 and step 3 (ADR 0019): kept in the host's memory cache for fifteen minutes under a
/// random 256-bit token together with the tenant it belongs to. The browser only ever posts the token back, so the rows
/// cannot be tampered with, and another salon's token is a 404 in the controller.
/// </summary>
public sealed class ImportSessionStore(IMemoryCache cache)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    private const string KeyPrefix = "client-import:";

    /// <summary>Stores a fresh session and returns its token: 64 lower-case hex characters.</summary>
    public string Save(ImportSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        Replace(token, session);
        return token;
    }

    /// <summary>Overwrites the session under an existing token; the fifteen minutes start again.</summary>
    public void Replace(string token, ImportSession session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(session);
        cache.Set(KeyPrefix + token, session, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Lifetime });
    }

    public ImportSession? Find(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return cache.TryGetValue(KeyPrefix + token, out ImportSession? session) ? session : null;
    }

    public void Remove(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        cache.Remove(KeyPrefix + token);
    }
}

/// <summary>What a token points at: the classified preview before confirm, the result after it; <see cref="TenantId"/> is checked on every use.</summary>
public sealed record ImportSession(Guid TenantId, ClientImportPreview? Preview, ClientImportResult? Result)
{
    public override string ToString() => $"{nameof(ImportSession)} {{ TenantId = {TenantId}, Stage = {(Result is null ? "preview" : "result")} }}";
}