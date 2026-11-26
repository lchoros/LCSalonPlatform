using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Salon.Api.Contracts.Booking;
using Salon.Core.Tasks;
using Salon.Services.Localization;
using Salon.Services.Notifications;
using Salon.Web.Framework.Api;
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
        services.AddScoped<IVisitBasket, VisitBasket>();
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
            // One policy, one count per address, whether the page or the booking API (ADR 0097) asked.
            limiter.AddPolicy(PublicBookingDefaults.SmsCodeRateLimitPolicy, new PerAddressPolicy(o => o.SmsCodeRateLimit));
            limiter.AddPolicy(PublicBookingDefaults.CodeVerifyRateLimitPolicy, new PerAddressPolicy(o => o.CodeVerifyRateLimit));
            limiter.AddPolicy(PublicBookingDefaults.DepositPayRateLimitPolicy, new PerAddressPolicy(o => o.DepositPayRateLimit));
        });

        return services;
    }

    /// <summary>
    /// A fixed window per address. A refusal says when to try again; a page gets the bare status and its status page
    /// (ADR 0087), the booking API an <see cref="ApiProblem"/> it can show. A policy's own rejection replaces the
    /// limiter's general one, which the sign-in registers last and so would otherwise decide.
    /// </summary>
    private sealed class PerAddressPolicy(Func<PublicBookingOptions, FixedWindowLimit> pick) : IRateLimiterPolicy<string>
    {
        public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

        public RateLimitPartition<string> GetPartition(HttpContext httpContext)
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

        private static async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
        {
            var http = context.HttpContext;
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            if (http.Request.Path.StartsWithSegments(BookingApi.Root, StringComparison.OrdinalIgnoreCase))
            {
                // The limiter runs before the salon's culture is set, so the sentence is in the default language.
                var localizer = http.RequestServices.GetRequiredService<ILocalizationService>();
                await http.Response.WriteAsJsonAsync(new ApiProblem(ApiProblemCodes.TooManyCodes, localizer["Booking.Api.TooManyRequests"]), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}