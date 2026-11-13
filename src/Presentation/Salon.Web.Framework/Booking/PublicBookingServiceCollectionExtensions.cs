using System.Globalization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Salon.Core.Tasks;
using Salon.Services.Notifications;
using Salon.Web.Framework.Tenancy;

namespace Salon.Web.Framework.Booking;

public static class PublicBookingServiceCollectionExtensions
{
    /// <summary>
    /// The host side of ADR 0014: the absolute-URL builder for messages and the two per-address rate-limit policies of the
    /// public booking flow (<see cref="PublicBookingDefaults.SmsCodeRateLimitPolicy"/>, <see cref="PublicBookingDefaults.CodeVerifyRateLimitPolicy"/>).
    /// Reads section <see cref="PublicBookingOptions.SectionName"/>. <c>AddRateLimiter</c> is additive, so this composes
    /// with the login policy registered by <c>AddSalonAuthentication</c>.
    /// </summary>
    public static IServiceCollection AddSalonPublicBooking(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<PublicBookingOptions>().BindConfiguration(PublicBookingOptions.SectionName);
        services.AddSingleton<IPublicUrlBuilder, PublicUrlBuilder>();
        services.AddSingleton<IReturningClientCookie, ReturningClientCookie>();
        // Retention of verification rows (ADR 0017): one recurring job like every IScheduleTask (ADR 0005).
        services.AddScoped<IScheduleTask, PhoneVerificationCleanupTask>();

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
            limiter.AddPolicy(PublicBookingDefaults.SmsCodeRateLimitPolicy, httpContext => PerAddress(httpContext, o => o.SmsCodeRateLimit));
            limiter.AddPolicy(PublicBookingDefaults.CodeVerifyRateLimitPolicy, httpContext => PerAddress(httpContext, o => o.CodeVerifyRateLimit));
        });

        return services;
    }

    private static RateLimitPartition<string> PerAddress(HttpContext httpContext, Func<PublicBookingOptions, FixedWindowLimit> pick)
    {
        var limit = pick(httpContext.RequestServices.GetRequiredService<IOptions<PublicBookingOptions>>().Value);
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                QueueLimit = 0,
            });
    }
}