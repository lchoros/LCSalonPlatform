using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using NodaTime;

namespace Salon.Web.Framework.ClientPortal;

/// <summary>
/// A client who proved their number (ADR 0083): which salon, which client card, and when the code was entered.
/// </summary>
public sealed record ClientPortalSession(Guid TenantId, Guid ClientId, Instant IssuedAt);

/// <summary>
/// The client's session, kept in a cookie of its own (ADR 0083). It is deliberately not an ASP.NET authentication
/// scheme: it never becomes <c>HttpContext.User</c>, so nothing that admits staff (<c>[RequirePermission]</c>, the
/// tenant middleware, the admin shell) can mistake a client for one, and the staff cookie is never read here.
/// </summary>
public interface IClientPortalSession
{
    /// <summary>
    /// The session for the salon in scope, or null: no cookie, a forged or damaged one, one written for another
    /// salon, one left idle longer than <see cref="ClientPortalDefaults.IdleLifetime"/> or older than
    /// <see cref="ClientPortalDefaults.AbsoluteLifetime"/>.
    /// </summary>
    ClientPortalSession? Read(HttpRequest request, Guid tenantId);

    /// <summary>Starts a session now, for a client who has just entered the right code.</summary>
    void Start(HttpResponse response, Guid tenantId, Guid clientId);

    /// <summary>Notes that the client is still here, which moves the idle limit and never the absolute one.</summary>
    void Slide(HttpResponse response, ClientPortalSession session);

    void Clear(HttpResponse response);
}

/// <summary>
/// The cookie carries the tenant id, the client id, the instant of sign-in and the instant last seen: no number, no
/// name. It is sealed with Data Protection under a purpose of its own, so it cannot be read, forged, or swapped with
/// the staff cookie or the returning-client cookie. HttpOnly, Secure, SameSite=Lax, no Domain (it never travels to
/// another salon's host) and bound to <see cref="ClientPortalDefaults.VisitsPath"/>. Both lifetimes are checked here
/// against <see cref="IClock"/>; the browser is not trusted with either, and is given no expiry date, so the cookie
/// also goes when the browser is closed.
/// </summary>
public sealed class ClientPortalSessionCookie(IDataProtectionProvider provider, IClock clock) : IClientPortalSession
{
    private const string Purpose = "Salon.ClientPortal.Session.v1";

    /// <summary>Two hosts of one site may disagree about the time by a moment; a cookie from further ahead is not ours.</summary>
    private static readonly Duration ClockSkew = Duration.FromMinutes(1);

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public ClientPortalSession? Read(HttpRequest request, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Cookies.TryGetValue(ClientPortalDefaults.CookieName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(value));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        {
            // Edited, cut short, sealed for another purpose (the staff cookie), or from before a key rotation.
            return null;
        }

        if (payload is null || payload.T != tenantId || payload.T == Guid.Empty || payload.C == Guid.Empty)
        {
            return null;
        }

        var now = clock.GetCurrentInstant();
        var issuedAt = Instant.FromUnixTimeTicks(payload.I);
        var seenAt = Instant.FromUnixTimeTicks(payload.S);
        var alive = issuedAt <= now + ClockSkew
            && seenAt >= issuedAt
            && seenAt <= now + ClockSkew
            && now - seenAt < ClientPortalDefaults.IdleLifetime
            && now - issuedAt < ClientPortalDefaults.AbsoluteLifetime;
        return alive ? new ClientPortalSession(payload.T, payload.C, issuedAt) : null;
    }

    public void Start(HttpResponse response, Guid tenantId, Guid clientId)
    {
        var now = clock.GetCurrentInstant();
        Write(response, new Payload(tenantId, clientId, now.ToUnixTimeTicks(), now.ToUnixTimeTicks()));
    }

    public void Slide(HttpResponse response, ClientPortalSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Write(response, new Payload(session.TenantId, session.ClientId, session.IssuedAt.ToUnixTimeTicks(), clock.GetCurrentInstant().ToUnixTimeTicks()));
    }

    public void Clear(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(ClientPortalDefaults.CookieName, Options());
    }

    private void Write(HttpResponse response, Payload payload)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(ClientPortalDefaults.CookieName, _protector.Protect(JsonSerializer.Serialize(payload)), Options());
    }

    private static CookieOptions Options() => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = ClientPortalDefaults.VisitsPath,
    };

    /// <summary>Tenant, client, issued and seen (Unix ticks). Short names keep the cookie small.</summary>
    private sealed record Payload(Guid T, Guid C, long I, long S);
}