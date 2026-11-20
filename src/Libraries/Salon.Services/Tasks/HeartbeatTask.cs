using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Salon.Core.Tasks;
using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Services.Tenancy;

namespace Salon.Services.Tasks;

/// <summary>
/// Hourly smoke task: proves the runner, the per-tenant scope and the tenant filter work, and says what kind of
/// database session the app really has (ADR 0090): whether a pooler shares server sessions between the app's
/// connections, and whether the login bypasses row-level security. Replaced by real tasks in M2.
/// </summary>
internal sealed class HeartbeatTask(ITenantScopeRunner tenants, IServiceScopeFactory scopeFactory, ILogger<HeartbeatTask> logger) : IScheduleTask
{
    public const string Name = "System.Heartbeat";

    public string SystemName => Name;

    public string CronExpression => "0 * * * *";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await ReportDatabaseSessionAsync(cancellationToken).ConfigureAwait(false);
        await tenants.ForEachTenantAsync(async (services, tenant, ct) =>
        {
            var count = await services.GetRequiredService<SalonDbContext>().Services.CountAsync(ct);
            logger.LogInformation("Heartbeat: tenant {Slug} has {Count} services", tenant.Slug, count);
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReportDatabaseSessionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var first = scopeFactory.CreateAsyncScope();
            await using var second = scopeFactory.CreateAsyncScope();
            var findings = await DatabaseSessionProbe.RunAsync(
                first.ServiceProvider.GetRequiredService<SalonDbContext>(),
                second.ServiceProvider.GetRequiredService<SalonDbContext>(),
                cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Heartbeat: server sessions shared by a pooler {Sharing}; the database login bypasses row-level security {Bypasses}",
                findings.Sharing,
                findings.LoginBypassesRowLevelSecurity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The probe only reports; the per-tenant part of the heartbeat must still run.
            logger.LogWarning(ex, "Heartbeat: the database session probe failed");
        }
    }
}