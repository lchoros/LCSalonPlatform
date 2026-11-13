using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Identity;
using Salon.Services.Identity;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// Browser-like clients for the sign-in flow: an https base address (the auth cookie is Secure, so the cookie container
/// would drop it over http), an Accept header that asks for HTML (so a challenge is a redirect, not a 401), and helpers
/// that create a user in a tenant and post the login form with its anti-forgery token.
/// </summary>
public static partial class IdentityTestClient
{
    public const string Password = "Correct-horse-9";
    public const string LoginPath = "/account/login";
    public const string CookieName = "salon.auth";

    public static HttpClient ClientFor(this SalonWebApplicationFactory factory, string host, bool handleCookies = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri($"https://{host}"),
            HandleCookies = handleCookies,
        });
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        return client;
    }

    public static async Task<User> CreateUserAsync(
        this SalonWebApplicationFactory factory,
        Guid tenantId,
        string email,
        Role role,
        string password = Password,
        string? displayName = null)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<IUserService>().CreateAsync(email, displayName ?? email, password, role);
    }

    /// <summary>GETs the login form for its anti-forgery token and POSTs the credentials with it. The redirect is not followed.</summary>
    public static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password, string? returnUrl = null)
    {
        var form = await client.GetAsync(new Uri(LoginPath, UriKind.Relative));
        form.StatusCode.ShouldBe(HttpStatusCode.OK, "the login form did not render");
        var fields = new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = AntiForgeryToken(await form.Content.ReadAsStringAsync()),
        };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return await client.PostAsync(new Uri(LoginPath, UriKind.Relative), new FormUrlEncodedContent(fields));
    }

    /// <summary>Creates a user with <paramref name="role"/> in the tenant and returns a client that holds its auth cookie.</summary>
    public static async Task<HttpClient> SignedInClientAsync(this SalonWebApplicationFactory factory, string host, Guid tenantId, Role role, string? email = null)
    {
        email ??= $"{role.ToString().ToLowerInvariant()}@{host}";
        await factory.CreateUserAsync(tenantId, email, role);
        var client = factory.ClientFor(host);
        var login = await LoginAsync(client, email, Password);
        login.StatusCode.ShouldBe(HttpStatusCode.Redirect, "sign-in failed: " + await login.Content.ReadAsStringAsync());
        return client;
    }

    public static string AntiForgeryToken(string html)
    {
        var match = AntiForgeryTokenPattern().Match(html);
        match.Success.ShouldBeTrue("the form has no anti-forgery token");
        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }

    /// <summary>The "salon.auth=value" pair from a response that issued the cookie, or null when none was set.</summary>
    public static string? AuthCookie(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiForgeryTokenPattern();
}