using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Salon.Core.Tasks;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Tasks;

/// <summary>Hourly smoke task: proves the runner, the per-tenant scope and the tenant filter work. Replaced by real tasks in M2.</summary>
internal sealed class HeartbeatTask(ITenantScopeRunner tenants, ILogger<HeartbeatTask> logger) : IScheduleTask
{
    public const string Name = "System.Heartbeat";

    public string SystemName => Name;

    public string CronExpression => "0 * * * *";

    public Task ExecuteAsync(CancellationToken cancellationToken)
        => tenants.ForEachTenantAsync(async (services, tenant, ct) =>
        {
            var count = await services.GetRequiredService<SalonDbContext>().Services.CountAsync(ct);
            logger.LogInformation("Heartbeat: tenant {Slug} has {Count} services", tenant.Slug, count);
        }, cancellationToken);
}