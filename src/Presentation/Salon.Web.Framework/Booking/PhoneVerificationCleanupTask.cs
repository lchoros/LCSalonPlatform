using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Booking;
using Salon.Core.Tasks;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Web.Framework.Booking;

/// <summary>
/// Hourly retention job of ADR 0017: deletes every <c>phone_verifications</c> row created before
/// <see cref="PhoneVerificationRules.RetentionCutOff"/> (<see cref="PublicBookingOptions.VerificationRetention"/>, a day by
/// default) in every active tenant, each inside its own tenant scope so the filter and RLS apply. After a day a row is spent,
/// dead or expired whatever its state, and the appointment it produced keeps its own record. Lives next to the options it
/// reads; the schedule task contract comes from Core (ADR 0005).
/// </summary>
internal sealed class PhoneVerificationCleanupTask(
    ITenantScopeRunner tenants,
    IOptions<PublicBookingOptions> options,
    IClock clock,
    ILogger<PhoneVerificationCleanupTask> logger) : IScheduleTask
{
    public const string Name = "Booking.CleanupVerifications";

    public string SystemName => Name;

    public string CronExpression => "0 * * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var cutOff = PhoneVerificationRules.RetentionCutOff(clock.GetCurrentInstant(), Duration.FromTimeSpan(options.Value.VerificationRetention));
        return tenants.ForEachTenantAsync(async (services, tenant, ct) =>
        {
            var deleted = await services.GetRequiredService<SalonDbContext>().PhoneVerifications
                .Where(v => v.CreatedAt < cutOff)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
            if (deleted > 0)
            {
                // Counts and the salon only: the rows held names and phone numbers.
                logger.LogInformation("Deleted {Count} phone verifications created before {CutOff} for tenant {Slug}", deleted, cutOff, tenant.Slug);
            }
        }, cancellationToken);
    }
}