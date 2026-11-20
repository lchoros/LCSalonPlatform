using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using NodaTime;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.ClientPortal;

namespace Salon.Tests.Unit.ClientPortal;

/// <summary>
/// The client's own cookie (ADR 0083): sealed, bound to one salon and to the client's pages, alive for thirty idle
/// minutes and twelve hours at most, and nothing that was not sealed for this purpose is ever read as a session.
/// </summary>
public class ClientPortalSessionCookieTests
{
    private static readonly Guid Studio = Guid.NewGuid();
    private static readonly Guid Olivia = Guid.NewGuid();
    private static readonly Instant SignedInAt = Instant.FromUtc(2026, 11, 20, 10, 0);

    private readonly EphemeralDataProtectionProvider _keys = new();
    private readonly MovableClock _clock = new(SignedInAt);
    private readonly ClientPortalSessionCookie _cookie;

    public ClientPortalSessionCookieTests() => _cookie = new ClientPortalSessionCookie(_keys, _clock);

    [Fact]
    public void A_started_session_reads_back_for_the_same_salon_with_the_instant_it_began()
    {
        var request = Started();

        _cookie.Read(request, Studio).ShouldBe(new ClientPortalSession(Studio, Olivia, SignedInAt));
    }

    [Fact]
    public void The_cookie_is_http_only_secure_lax_host_only_bound_to_the_clients_pages_and_has_no_date()
    {
        var context = new DefaultHttpContext();
        _cookie.Start(context.Response, Studio, Olivia);

        var header = context.Response.Headers.SetCookie.ToString();

        header.ShouldStartWith(ClientPortalDefaults.CookieName + "=");
        header.ShouldContain("httponly", Case.Insensitive);
        header.ShouldContain("secure", Case.Insensitive);
        header.ShouldContain("samesite=lax", Case.Insensitive);
        header.ShouldContain("path=/my-bookings", Case.Insensitive);
        header.ShouldNotContain("domain=", Case.Insensitive);
        header.ShouldNotContain("expires=", Case.Insensitive);
        header.ShouldNotContain("max-age=", Case.Insensitive);
        header.ShouldNotContain(Olivia.ToString(), Case.Insensitive, "the card's id is sealed, never plain text");
        header.ShouldNotContain(Studio.ToString(), Case.Insensitive);
    }

    [Fact]
    public void Another_salon_a_missing_cookie_and_a_made_up_value_read_as_nothing()
    {
        var request = Started();

        _cookie.Read(request, Guid.NewGuid()).ShouldBeNull("started for Studio, read for another salon");
        _cookie.Read(new DefaultHttpContext().Request, Studio).ShouldBeNull("no cookie");
        _cookie.Read(RequestWith("not-a-protected-value"), Studio).ShouldBeNull("made up");
        _cookie.Read(RequestWith(string.Empty), Studio).ShouldBeNull("empty");
    }

    [Fact]
    public void A_value_sealed_for_another_purpose_or_with_another_key_is_not_a_session()
    {
        // The returning-client cookie (ADR 0014) is sealed by the same key ring under its own purpose.
        var prefill = new DefaultHttpContext();
        new ReturningClientCookie(_keys).Write(prefill.Response, new ReturningClient(Studio, "Olivia", "Brown", "07700 900123", null));
        var prefillValue = ValueOf(prefill.Response);

        _cookie.Read(RequestWith(prefillValue), Studio).ShouldBeNull("sealed for the booking form's prefill");

        // The same payload shape, sealed under the staff cookie's kind of purpose.
        var foreign = _keys.CreateProtector("Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationMiddleware", "Cookies", "v2")
            .Protect($$"""{"T":"{{Studio}}","C":"{{Olivia}}","I":{{SignedInAt.ToUnixTimeTicks()}},"S":{{SignedInAt.ToUnixTimeTicks()}}}""");
        _cookie.Read(RequestWith(foreign), Studio).ShouldBeNull("sealed for another purpose");

        var otherRing = new DefaultHttpContext();
        new ClientPortalSessionCookie(new EphemeralDataProtectionProvider(), _clock).Start(otherRing.Response, Studio, Olivia);
        _cookie.Read(RequestWith(ValueOf(otherRing.Response)), Studio).ShouldBeNull("sealed with keys that are not ours");
    }

    [Fact]
    public void Thirty_idle_minutes_end_the_session()
    {
        var request = Started();

        _clock.Now = SignedInAt + Duration.FromMinutes(30) - Duration.FromSeconds(1);
        _cookie.Read(request, Studio).ShouldNotBeNull("a second short of the limit");

        _clock.Now = SignedInAt + Duration.FromMinutes(30);
        _cookie.Read(request, Studio).ShouldBeNull("thirty minutes without a visit");
    }

    [Fact]
    public void Coming_back_moves_the_idle_limit_and_never_the_twelve_hours()
    {
        var request = Started();
        var session = _cookie.Read(request, Studio).ShouldNotBeNull();

        // A visit every 25 minutes keeps the session going, and it still began when the code was entered.
        var steps = 0;
        while (_clock.Now + Duration.FromMinutes(25) < SignedInAt + ClientPortalDefaults.AbsoluteLifetime)
        {
            _clock.Now += Duration.FromMinutes(25);
            var seen = new DefaultHttpContext();
            _cookie.Slide(seen.Response, session);
            request = RequestWith(ValueOf(seen.Response));
            session = _cookie.Read(request, Studio).ShouldNotBeNull($"after {++steps} visits");
            session.IssuedAt.ShouldBe(SignedInAt);
        }

        steps.ShouldBe(28);
        _clock.Now = SignedInAt + ClientPortalDefaults.AbsoluteLifetime;
        _cookie.Read(request, Studio).ShouldBeNull("twelve hours after the code, however recently it was used");
    }

    [Fact]
    public void A_cookie_from_the_future_is_not_ours()
    {
        var request = Started();

        _clock.Now = SignedInAt - Duration.FromSeconds(30);
        _cookie.Read(request, Studio).ShouldNotBeNull("two hosts may disagree by a moment");

        _clock.Now = SignedInAt - Duration.FromMinutes(2);
        _cookie.Read(request, Studio).ShouldBeNull();
    }

    [Fact]
    public void Clear_expires_the_cookie_on_the_path_it_was_set_for()
    {
        var context = new DefaultHttpContext();

        _cookie.Clear(context.Response);

        var header = context.Response.Headers.SetCookie.ToString();
        header.ShouldStartWith(ClientPortalDefaults.CookieName + "=;");
        header.ShouldContain("expires=", Case.Insensitive);
        header.ShouldContain("path=/my-bookings", Case.Insensitive);
    }

    [Fact]
    public void The_lifetimes_are_thirty_minutes_and_twelve_hours()
    {
        ClientPortalDefaults.IdleLifetime.ShouldBe(Duration.FromMinutes(30));
        ClientPortalDefaults.AbsoluteLifetime.ShouldBe(Duration.FromHours(12));
        ClientPortalDefaults.CookieName.ShouldNotBe("salon.auth");
    }

    private HttpRequest Started()
    {
        var context = new DefaultHttpContext();
        _cookie.Start(context.Response, Studio, Olivia);
        return RequestWith(ValueOf(context.Response));
    }

    private static string ValueOf(HttpResponse response)
    {
        var header = response.Headers.SetCookie.ToString();
        var pair = header[..header.IndexOf(';', StringComparison.Ordinal)];
        return pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..];
    }

    private static HttpRequest RequestWith(string value)
    {
        var next = new DefaultHttpContext();
        next.Request.Headers.Cookie = $"{ClientPortalDefaults.CookieName}={value}";
        return next.Request;
    }

    private sealed class MovableClock(Instant start) : IClock
    {
        public Instant Now { get; set; } = start;

        public Instant GetCurrentInstant() => Now;
    }
}