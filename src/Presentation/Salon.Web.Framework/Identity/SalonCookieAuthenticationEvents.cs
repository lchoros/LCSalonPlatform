using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Identity;
using Salon.Services.Identity;
using Salon.Services.Tenancy;

namespace Salon.Web.Framework.Identity;

/// <summary>
/// The cookie's hooks (ADR 0009). On validation the user row is re-read at most once per
/// <see cref="SalonAuthenticationOptions.ValidationInterval"/> per cookie (tracked by a <c>validated_at</c> claim) and the
/// session ends when the user is gone, inactive or carries a different security stamp. Redirects to the login and denied
/// pages become bare 401/403 for requests that do not ask for HTML. Resolved per request through
/// <see cref="CookieAuthenticationOptions.EventsType"/>.
/// </summary>
public sealed class SalonCookieAuthenticationEvents(
    ITenantScopeRunner tenantScopes,
    IClock clock,
    IOptions<SalonAuthenticationOptions> options,
    ILogger<SalonCookieAuthenticationEvents> logger) : CookieAuthenticationEvents
{
    public override Task SigningIn(CookieSigningInContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal?.Identity is ClaimsIdentity identity)
        {
            StampValidatedAt(identity, clock.GetCurrentInstant());
        }

        return base.SigningIn(context);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Principal?.Identity is not ClaimsIdentity identity)
        {
            return;
        }

        var now = clock.GetCurrentInstant();
        if (ReadValidatedAt(identity) is { } validatedAt && now - validatedAt < Duration.FromTimeSpan(options.Value.ValidationInterval))
        {
            return;
        }

        if (!await IsStillValidAsync(identity, context.HttpContext.RequestAborted))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(context.Scheme.Name);
            return;
        }

        // The user row is still what the cookie says: note the time and re-issue the cookie so the claim travels with it.
        StampValidatedAt(identity, now);
        context.ShouldRenew = true;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!AcceptsHtml(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        return base.RedirectToLogin(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!AcceptsHtml(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        return base.RedirectToAccessDenied(context);
    }

    /// <summary>Browsers always list text/html; fetch, htmx and API clients do not and want a status code they can act on.</summary>
    internal static bool AcceptsHtml(HttpRequest request)
        => request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);

    internal static Instant? ReadValidatedAt(ClaimsIdentity identity)
        => long.TryParse(identity.FindFirst(SalonAuthenticationDefaults.ValidatedAtClaimType)?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? Instant.FromUnixTimeSeconds(seconds)
            : null;

    private static void StampValidatedAt(ClaimsIdentity identity, Instant now)
    {
        foreach (var stale in identity.FindAll(SalonAuthenticationDefaults.ValidatedAtClaimType).ToList())
        {
            identity.RemoveClaim(stale);
        }

        identity.AddClaim(new Claim(SalonAuthenticationDefaults.ValidatedAtClaimType, now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Reads the user in a scope of the cookie's own tenant: authentication runs before the tenant middleware, so the
    /// request scope has no tenant yet and the filtered <c>Users</c> set would match nothing.
    /// </summary>
    private async Task<bool> IsStillValidAsync(ClaimsIdentity identity, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(identity.FindFirst(UserClaimTypes.Subject)?.Value, out var userId)
            || !Guid.TryParse(identity.FindFirst(UserClaimTypes.TenantId)?.Value, out var tenantId)
            || identity.FindFirst(UserClaimTypes.SecurityStamp)?.Value is not { Length: > 0 } stamp)
        {
            logger.LogWarning("Auth cookie without a usable sub, tenant_id or security_stamp claim was rejected");
            return false;
        }

        User? user = null;
        await tenantScopes.RunAsync(
            tenantId,
            async (provider, ct) => user = await provider.GetRequiredService<IUserService>().FindByIdAsync(userId, ct),
            cancellationToken);

        if (user is null)
        {
            logger.LogInformation("Auth cookie for missing user {UserId} of tenant {TenantId} was rejected", userId, tenantId);
            return false;
        }

        if (!user.IsActive)
        {
            logger.LogInformation("Auth cookie for inactive user {UserId} was rejected", userId);
            return false;
        }

        if (!string.Equals(user.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            logger.LogInformation("Auth cookie for user {UserId} carries an outdated security stamp and was rejected", userId);
            return false;
        }

        return true;
    }
}