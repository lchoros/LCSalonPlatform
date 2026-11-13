using System.Text.Json;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Salon.Web.Framework.Booking;

/// <summary>
/// What a returning client typed last time on this device (ADR 0014, prefill): kept only in a cookie on the client's own
/// browser, protected with Data Protection so it cannot be read or forged, scoped to the salon it was written for. The
/// server never looks a client up by it; it only fills the details form so a regular takes three taps and no typing.
/// </summary>
public interface IReturningClientCookie
{
    /// <summary>The remembered details for the salon in scope, or null when there is no cookie, it is tampered with, or it belongs to another salon.</summary>
    ReturningClient? Read(HttpRequest request, Guid tenantId);

    void Write(HttpResponse response, ReturningClient client);

    void Forget(HttpResponse response);
}

public sealed record ReturningClient(Guid TenantId, string FirstName, string LastName, string Phone, string? Email);

public sealed class ReturningClientCookie(IDataProtectionProvider provider) : IReturningClientCookie
{
    public const string CookieName = "salon.returning";

    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private const string Purpose = "Salon.Booking.ReturningClient.v1";

    private readonly IDataProtector _protector = provider.CreateProtector(Purpose);

    public ReturningClient? Read(HttpRequest request, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(value));
            if (payload is null || payload.T != tenantId || string.IsNullOrWhiteSpace(payload.F) || string.IsNullOrWhiteSpace(payload.P))
            {
                return null;
            }

            return new ReturningClient(payload.T, payload.F, payload.L ?? string.Empty, payload.P, string.IsNullOrWhiteSpace(payload.E) ? null : payload.E);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            // A cookie from before a key rotation, or one somebody edited: the form simply starts empty.
            return null;
        }
    }

    public void Write(HttpResponse response, ReturningClient client)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(client);
        var payload = new Payload(client.TenantId, client.FirstName.Trim(), client.LastName.Trim(), client.Phone.Trim(), client.Email?.Trim());
        response.Cookies.Append(CookieName, _protector.Protect(JsonSerializer.Serialize(payload)), Options(response, Lifetime));
    }

    public void Forget(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Delete(CookieName, Options(response, null));
    }

    private static CookieOptions Options(HttpResponse response, TimeSpan? lifetime) => new()
    {
        HttpOnly = true,
        IsEssential = false,
        SameSite = SameSiteMode.Lax,
        Secure = response.HttpContext.Request.IsHttps,
        Path = "/book",
        MaxAge = lifetime,
    };

    /// <summary>Short names keep the cookie small; the protector adds its own overhead.</summary>
    private sealed record Payload(Guid T, string F, string? L, string P, string? E);
}