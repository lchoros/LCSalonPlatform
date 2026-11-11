using System.Net;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Identity;

/// <summary>
/// The Login and Logout slices over HTTP (ADR 0009): cookie issued on success, local-only return URL, identical answers
/// for a wrong password and an unknown email, lockout after five failures, no login on the platform domain, per-IP rate limit.
/// </summary>
[Collection(SharedDatabase.Name)]
public partial class LoginTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private const string Host = "studio-20.localhost";
    private const string Email = "owner@studio-20.local";

    private Tenant _studio = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _studio = await factory.CreateTenantAsync("studio-20", name: "Studio 20");
        await factory.CreateUserAsync(_studio.Id, Email, Role.Owner, displayName: "Olivia Owner");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_form_renders_in_the_salon_language_with_an_anti_forgery_token()
    {
        var polish = await factory.CreateTenantAsync("salon-pl", "pl-PL", "PLN", "Salon PL");
        polish.ShouldNotBeNull();
        using var english = factory.ClientFor(Host);
        using var polishClient = factory.ClientFor("salon-pl.localhost");

        var englishHtml = await (await english.GetAsync(new Uri("/account/login", UriKind.Relative))).Content.ReadAsStringAsync();
        var polishHtml = await (await polishClient.GetAsync(new Uri("/account/login", UriKind.Relative))).Content.ReadAsStringAsync();

        englishHtml.ShouldContain("data-testid=\"login-form\"");
        englishHtml.ShouldContain("__RequestVerificationToken");
        englishHtml.ShouldContain("Sign in");
        polishHtml.ShouldContain("Zaloguj się");
    }

    [Fact]
    public async Task A_correct_password_sets_the_cookie_and_follows_a_local_return_url()
    {
        using var client = factory.ClientFor(Host);

        var response = await IdentityTestClient.LoginAsync(client, Email, IdentityTestClient.Password, "/admin/services/new");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/admin/services/new");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("salon.auth=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldNotContain("domain=", Case.Insensitive);

        var home = await client.GetAsync(new Uri("/", UriKind.Relative));
        var html = await home.Content.ReadAsStringAsync();
        html.ShouldContain("data-testid=\"user-name\"");
        html.ShouldContain("Olivia Owner");
        html.ShouldContain("data-testid=\"sign-out\"");
    }

    [Fact]
    public async Task Email_is_matched_case_insensitively_and_last_login_is_recorded()
    {
        using var client = factory.ClientFor(Host);

        var response = await IdentityTestClient.LoginAsync(client, "  Owner@Studio-20.LOCAL ", IdentityTestClient.Password);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await UserAsync()).LastLoginAt.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/x")]
    [InlineData("javascript:alert(1)")]
    public async Task An_external_return_url_is_ignored(string returnUrl)
    {
        using var client = factory.ClientFor(Host);

        var response = await IdentityTestClient.LoginAsync(client, Email, IdentityTestClient.Password, returnUrl);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/services");
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_answer()
    {
        using var client = factory.ClientFor(Host);

        var wrongPassword = await IdentityTestClient.LoginAsync(client, Email, "not-the-password");
        var unknownEmail = await IdentityTestClient.LoginAsync(client, "nobody@studio-20.local", IdentityTestClient.Password);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.OK);
        IdentityTestClient.AuthCookie(wrongPassword).ShouldBeNull();
        IdentityTestClient.AuthCookie(unknownEmail).ShouldBeNull();
        var wrongHtml = await wrongPassword.Content.ReadAsStringAsync();
        wrongHtml.ShouldContain("The email or password is incorrect.");
        Normalize(wrongHtml).ShouldBe(Normalize(await unknownEmail.Content.ReadAsStringAsync()));
        (await UserAsync()).FailedLoginCount.ShouldBe(1);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_so_the_right_password_is_refused_with_a_different_message()
    {
        using var client = factory.ClientFor(Host);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await IdentityTestClient.LoginAsync(client, Email, "wrong-" + attempt)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var user = await UserAsync();
        user.LockedOutUntil.ShouldNotBeNull();
        user.FailedLoginCount.ShouldBe(0);

        var locked = await IdentityTestClient.LoginAsync(client, Email, IdentityTestClient.Password);
        locked.StatusCode.ShouldBe(HttpStatusCode.OK);
        IdentityTestClient.AuthCookie(locked).ShouldBeNull();
        var html = await locked.Content.ReadAsStringAsync();
        html.ShouldContain("Too many failed attempts");
        html.ShouldNotContain("The email or password is incorrect.");
    }

    [Fact]
    public async Task Empty_fields_are_validation_messages_and_do_not_count_as_failures()
    {
        using var client = factory.ClientFor(Host);

        var response = await IdentityTestClient.LoginAsync(client, string.Empty, string.Empty);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Enter your email.");
        html.ShouldContain("Enter your password.");
        (await UserAsync()).FailedLoginCount.ShouldBe(0);
    }

    [Fact]
    public async Task A_post_without_the_anti_forgery_token_is_refused()
    {
        using var client = factory.ClientFor(Host);

        var response = await client.PostAsync(
            new Uri("/account/login", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = Email, ["Password"] = IdentityTestClient.Password }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        IdentityTestClient.AuthCookie(response).ShouldBeNull();
    }

    [Fact]
    public async Task The_platform_domain_has_no_login()
    {
        using var client = factory.ClientFor("localhost");

        (await client.GetAsync(new Uri("/account/login", UriKind.Relative))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Logout_clears_the_cookie_and_the_next_request_is_anonymous()
    {
        using var client = factory.ClientFor(Host);
        (await IdentityTestClient.LoginAsync(client, Email, IdentityTestClient.Password)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var home = await (await client.GetAsync(new Uri("/", UriKind.Relative))).Content.ReadAsStringAsync();

        var response = await client.PostAsync(
            new Uri("/account/logout", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = IdentityTestClient.AntiForgeryToken(home) }));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/");
        response.Headers.GetValues("Set-Cookie").ShouldContain(v => v.StartsWith("salon.auth=;", StringComparison.Ordinal) || v.Contains("salon.auth=; expires", StringComparison.OrdinalIgnoreCase));
        (await (await client.GetAsync(new Uri("/", UriKind.Relative))).Content.ReadAsStringAsync()).ShouldNotContain("data-testid=\"user-name\"");
    }

    [Fact]
    public async Task A_signed_in_user_opening_the_login_page_is_sent_on()
    {
        using var client = factory.ClientFor(Host);
        await IdentityTestClient.LoginAsync(client, Email, IdentityTestClient.Password);

        var response = await client.GetAsync(new Uri("/account/login", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe("/services");
    }

    [Fact]
    public async Task Rapid_login_posts_from_one_address_are_rate_limited()
    {
        // The shared host raises the limit so the other tests never trip it; this host uses the production default of ten per minute.
        using var limited = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:LoginRateLimit:PermitLimit"] = "10" })));
        using var client = limited.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{Host}") });
        var form = await client.GetAsync(new Uri("/account/login", UriKind.Relative));
        var token = IdentityTestClient.AntiForgeryToken(await form.Content.ReadAsStringAsync());

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 11; attempt++)
        {
            var response = await client.PostAsync(
                new Uri("/account/login", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "x@y.z", ["Password"] = "wrong", ["__RequestVerificationToken"] = token }));
            statuses.Add(response.StatusCode);
        }

        statuses.ShouldContain(HttpStatusCode.TooManyRequests);
        statuses.Take(10).ShouldAllBe(s => s == HttpStatusCode.OK);
    }

    private async Task<User> UserAsync()
    {
        await using var scope = factory.CreateTenantScope(_studio.Id);
        return (await scope.ServiceProvider.GetRequiredService<IUserService>().FindByEmailAsync(Email)).ShouldNotBeNull();
    }

    /// <summary>Strips the two values that legitimately differ between two renders: the anti-forgery token and the echoed email.</summary>
    private static string Normalize(string html)
        => InputValuePattern().Replace(html, "value=\"\"");

    [GeneratedRegex("value=\"[^\"]*\"")]
    private static partial Regex InputValuePattern();
}