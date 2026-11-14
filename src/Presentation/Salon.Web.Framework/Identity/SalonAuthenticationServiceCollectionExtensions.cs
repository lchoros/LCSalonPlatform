using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Salon.Services.Identity;

namespace Salon.Web.Framework.Identity;

public static class SalonAuthenticationServiceCollectionExtensions
{
    /// <summary>
    /// Cookie sign-in (ADR 0009): the <c>salon.auth</c> cookie (HttpOnly, Secure, SameSite=Lax, host-only, 8 h sliding),
    /// the password hasher, the user and sign-in services, authorization, and the per-address rate limit for the login
    /// POST. Reads section <see cref="SalonAuthenticationOptions.SectionName"/>.
    /// </summary>
    public static IServiceCollection AddSalonAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SalonAuthenticationOptions>(configuration.GetSection(SalonAuthenticationOptions.SectionName));

        // Default PasswordHasherOptions: PBKDF2-HMAC-SHA512, 100 000 iterations (.NET 10), identity v3 format.
        services.AddOptions<PasswordHasherOptions>();
        services.AddSingleton<IPasswordService, PasswordHasherPasswordService>();
        services.AddSalonIdentity();

        services.AddScoped<SalonCookieAuthenticationEvents>();
        services.AddAuthentication(SalonAuthenticationDefaults.Scheme)
            .AddCookie(SalonAuthenticationDefaults.Scheme, cookie =>
            {
                cookie.Cookie.Name = SalonAuthenticationDefaults.CookieName;
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.IsEssential = true;
                // No Domain: a host-only cookie never travels from one salon's subdomain to another's.
                cookie.ExpireTimeSpan = SalonAuthenticationDefaults.SessionLength;
                cookie.SlidingExpiration = true;
                cookie.LoginPath = SalonAuthenticationDefaults.LoginPath;
                cookie.AccessDeniedPath = SalonAuthenticationDefaults.AccessDeniedPath;
                cookie.ReturnUrlParameter = SalonAuthenticationDefaults.ReturnUrlParameter;
                cookie.EventsType = typeof(SalonCookieAuthenticationEvents);
            });
        services.AddAuthorization();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
            limiter.AddPolicy(SalonAuthenticationDefaults.LoginRateLimitPolicy, httpContext =>
            {
                var limits = httpContext.RequestServices.GetRequiredService<IOptions<SalonAuthenticationOptions>>().Value.LoginRateLimit;
                return RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = limits.Window,
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }

    /// <summary>
    /// Reads the cookie into <c>HttpContext.User</c>. Place <b>before</b> <c>UseSalonTenancy()</c>: the tenant middleware
    /// compares the cookie's tenant claim with the host's tenant and drops a cookie that belongs elsewhere.
    /// </summary>
    public static IApplicationBuilder UseSalonAuthentication(this IApplicationBuilder app) => app.UseAuthentication();
}