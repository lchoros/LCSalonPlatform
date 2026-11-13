using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

using Salon.Web.Framework.Booking;

namespace Salon.Tests.Unit.Booking;

/// <summary>The prefill cookie (ADR 0014): device-only, protected, salon-scoped, and silently ignored when it is not ours.</summary>
public class ReturningClientCookieTests
{
    private static readonly Guid Studio = Guid.NewGuid();

    private readonly ReturningClientCookie _cookie = new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Round_trips_the_details_for_the_same_salon_and_trims_them()
    {
        var context = new DefaultHttpContext();
        _cookie.Write(context.Response, new ReturningClient(Studio, " Olivia ", "Brown", " 07700 900123 ", "olivia@example.com"));

        var read = _cookie.Read(RequestCarrying(context.Response), Studio);

        read.ShouldBe(new ReturningClient(Studio, "Olivia", "Brown", "07700 900123", "olivia@example.com"));
    }

    [Fact]
    public void The_cookie_is_http_only_lax_and_limited_to_the_booking_pages()
    {
        var context = new DefaultHttpContext();
        _cookie.Write(context.Response, new ReturningClient(Studio, "Olivia", "Brown", "07700 900123", null));

        var header = context.Response.Headers.SetCookie.ToString();

        header.ShouldStartWith(ReturningClientCookie.CookieName + "=");
        header.ShouldContain("httponly", Case.Insensitive);
        header.ShouldContain("samesite=lax", Case.Insensitive);
        header.ShouldContain("path=/book", Case.Insensitive);
        header.ShouldContain("max-age=31536000", Case.Insensitive);
        header.Contains("Olivia", StringComparison.Ordinal).ShouldBeFalse("the details are protected, never plain text");
    }

    [Fact]
    public void Another_salon_a_tampered_value_and_a_missing_cookie_read_as_nothing()
    {
        var context = new DefaultHttpContext();
        _cookie.Write(context.Response, new ReturningClient(Studio, "Olivia", "Brown", "07700 900123", null));
        var request = RequestCarrying(context.Response);

        _cookie.Read(request, Guid.NewGuid()).ShouldBeNull("written for Studio, read for another salon");
        _cookie.Read(new DefaultHttpContext().Request, Studio).ShouldBeNull("no cookie");

        var tampered = new DefaultHttpContext();
        tampered.Request.Headers.Cookie = $"{ReturningClientCookie.CookieName}=not-a-protected-value";
        _cookie.Read(tampered.Request, Studio).ShouldBeNull("tampered");
    }

    [Fact]
    public void Forget_expires_the_cookie()
    {
        var context = new DefaultHttpContext();

        _cookie.Forget(context.Response);

        var header = context.Response.Headers.SetCookie.ToString();
        header.ShouldStartWith(ReturningClientCookie.CookieName + "=");
        header.ShouldContain("expires=", Case.Insensitive);
    }

    /// <summary>A request that sends back whatever the response just set.</summary>
    private static HttpRequest RequestCarrying(HttpResponse response)
    {
        var header = response.Headers.SetCookie.ToString();
        var pair = header[..header.IndexOf(';', StringComparison.Ordinal)];
        var next = new DefaultHttpContext();
        next.Request.Headers.Cookie = pair;
        return next.Request;
    }
}