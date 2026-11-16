using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Tasks;
using Salon.Services.Tenancy;

namespace Salon.Services.Notifications;

/// <summary>
/// Every five minutes, salon by salon: send the day's summary to the owners once its time has come (ADR 0050). A
/// salon that has not switched it on costs one settings read.
/// </summary>
internal sealed class DailySummaryTask(ITenantScopeRunner tenants, IClock clock, ILogger<DailySummaryTask> logger) : IScheduleTask
{
    public const string Name = "Notifications.SendDailySummaries";

    public string SystemName => Name;

    public string CronExpression => "*/5 * * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetCurrentInstant();
        return tenants.ForEachTenantAsync(async (services, tenant, ct) =>
        {
            var sent = await services.GetRequiredService<IDailySummaryService>().SendIfDueAsync(now, ct).ConfigureAwait(false);
            if (sent is { } owners)
            {
                logger.LogInformation("Sent the daily summary for tenant {Slug} to {Count} owners", tenant.Slug, owners);
            }
        }, cancellationToken);
    }
}