using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NodaTime;

using NSubstitute;

using Salon.Core.Identity;
using Salon.Services.Identity;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Identity;

namespace Salon.Tests.Unit.Identity;

/// <summary>
/// The cookie validation hook (ADR 0009): the user row is consulted at most once per interval, and the session ends when
/// the user is gone, inactive or carries another security stamp. Non-HTML requests get status codes, not redirects.
/// </summary>
public class SalonCookieAuthenticationEventsTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Instant Now = Instant.FromUtc(2026, 11, 11, 12, 0);

    private readonly IUserService _users = Substitute.For<IUserService>();
    private readonly IAuthenticationService _authentication = Substitute.For<IAuthenticationService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly User _user = new()
    {
        TenantId = TenantId,
        Email = "owner@studio-20.local",
        DisplayName = "Owner",
        PasswordHash = "hash",
        Role = Role.Owner,
        SecurityStamp = "stamp-1",
    };

    private SalonCookieAuthenticationEvents CreateEvents(TimeSpan? interval = null)
    {
        _clock.GetCurrentInstant().Returns(Now);
        // RunAsync hands the callback a provider holding the user service, as the real runner would in a tenant scope.
        var runner = Substitute.For<ITenantScopeRunner>();
        var provider = new ServiceCollection().AddSingleton(_users).BuildServiceProvider();
        runner.RunAsync(Arg.Any<Guid>(), Arg.Any<Func<IServiceProvider, CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<IServiceProvider, CancellationToken, Task>>()(provider, CancellationToken.None));

        return new SalonCookieAuthenticationEvents(
            runner,
            _clock,
            Options.Create(new SalonAuthenticationOptions { ValidationInterval = interval ?? TimeSpan.FromMinutes(5) }),
            NullLogger<SalonCookieAuthenticationEvents>.Instance);
    }

    [Fact]
    public async Task Signing_in_stamps_the_validation_time()
    {
        var principal = ClaimsFactory.Create(_user, TenantId);
        var context = new CookieSigningInContext(HttpContext(), Scheme(), new CookieAuthenticationOptions(), principal, new AuthenticationProperties(), new CookieOptions());

        await CreateEvents().SigningIn(context);

        principal.FindFirst("validated_at")!.Value.ShouldBe(Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task A_recently_validated_cookie_is_not_checked_against_the_database()
    {
        var context = ValidateContext(validatedAt: Now - Duration.FromMinutes(4));

        await CreateEvents().ValidatePrincipal(context);

        context.Principal.ShouldNotBeNull();
        await _users.DidNotReceive().FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        context.ShouldRenew.ShouldBeFalse();
    }

    [Fact]
    public async Task A_cookie_past_the_interval_is_checked_and_restamped_when_the_user_is_unchanged()
    {
        _users.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        var context = ValidateContext(validatedAt: Now - Duration.FromMinutes(6));

        await CreateEvents().ValidatePrincipal(context);

        context.Principal.ShouldNotBeNull();
        context.ShouldRenew.ShouldBeTrue();
        context.Principal.FindAll("validated_at").Single().Value.ShouldBe(Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        await _authentication.DidNotReceive().SignOutAsync(Arg.Any<HttpContext>(), Arg.Any<string?>(), Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task A_cookie_without_a_validation_time_is_checked_at_once()
    {
        _users.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        var context = ValidateContext(validatedAt: null);

        await CreateEvents().ValidatePrincipal(context);

        await _users.Received(1).FindByIdAsync(_user.Id, Arg.Any<CancellationToken>());
        context.Principal.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_zero_interval_checks_every_request()
    {
        _users.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        var context = ValidateContext(validatedAt: Now);

        await CreateEvents(TimeSpan.Zero).ValidatePrincipal(context);

        await _users.Received(1).FindByIdAsync(_user.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_missing_user_is_rejected_and_signed_out()
    {
        _users.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns((User?)null);
        var context = ValidateContext(validatedAt: null);

        await CreateEvents().ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
        await _authentication.Received(1).SignOutAsync(context.HttpContext, "salon.auth", Arg.Any<AuthenticationProperties?>());
    }

    [Fact]
    public async Task An_inactive_user_is_rejected()
    {
        _user.IsActive = false;
        _users.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);
        var context = ValidateContext(validatedAt: null);

        await CreateEvents().ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
    }

    [Fact]
    public async Task A_changed_security_stamp_is_rejected()
    {
        var context = ValidateContext(validatedAt: null);
        _user.SecurityStamp = "stamp-2";
        _users.FindByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

        await CreateEvents().ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
    }

    [Fact]
    public async Task A_cookie_without_the_identity_claims_is_rejected_without_a_lookup()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(UserClaimTypes.Name, "x")], "salon.auth"));
        var context = new CookieValidatePrincipalContext(HttpContext(), Scheme(), new CookieAuthenticationOptions(), new AuthenticationTicket(principal, "salon.auth"));

        await CreateEvents().ValidatePrincipal(context);

        context.Principal.ShouldBeNull();
        await _users.DidNotReceive().FindByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("text/html,application/xhtml+xml", true)]
    [InlineData("application/json", false)]
    [InlineData("*/*", false)]
    [InlineData("", false)]
    public void Only_requests_that_accept_html_are_redirected(string accept, bool html)
    {
        var context = new DefaultHttpContext();
        if (accept.Length > 0)
        {
            context.Request.Headers.Accept = accept;
        }

        SalonCookieAuthenticationEvents.AcceptsHtml(context.Request).ShouldBe(html);
    }

    [Fact]
    public async Task A_non_html_challenge_is_a_401_and_a_non_html_forbid_is_a_403()
    {
        var login = Redirect("application/json");
        var denied = Redirect("application/json");

        await CreateEvents().RedirectToLogin(login);
        await CreateEvents().RedirectToAccessDenied(denied);

        login.Response.StatusCode.ShouldBe(StatusCodes.Status401Unauthorized);
        login.Response.Headers.Location.ToString().ShouldBeEmpty();
        denied.Response.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task An_html_challenge_redirects_to_the_login_page()
    {
        var login = Redirect("text/html");

        await CreateEvents().RedirectToLogin(login);

        login.Response.StatusCode.ShouldBe(StatusCodes.Status302Found);
        login.Response.Headers.Location.ToString().ShouldBe("https://studio-20.localhost/account/login?returnUrl=%2Fadmin");
    }

    private CookieValidatePrincipalContext ValidateContext(Instant? validatedAt)
    {
        var principal = ClaimsFactory.Create(_user, TenantId);
        if (validatedAt is { } at)
        {
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("validated_at", at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        return new CookieValidatePrincipalContext(HttpContext(), Scheme(), new CookieAuthenticationOptions(), new AuthenticationTicket(principal, "salon.auth"));
    }

    private static RedirectContext<CookieAuthenticationOptions> Redirect(string accept)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.Accept = accept;
        return new RedirectContext<CookieAuthenticationOptions>(
            httpContext,
            Scheme(),
            new CookieAuthenticationOptions(),
            new AuthenticationProperties(),
            "https://studio-20.localhost/account/login?returnUrl=%2Fadmin");
    }

    private DefaultHttpContext HttpContext() => new()
    {
        RequestServices = new ServiceCollection().AddSingleton(_authentication).BuildServiceProvider(),
    };

    private static AuthenticationScheme Scheme() => new("salon.auth", null, typeof(CookieAuthenticationHandler));
}