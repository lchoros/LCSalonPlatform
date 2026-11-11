using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Services.Tenancy;

internal sealed class TenantScopeRunner(IServiceScopeFactory scopeFactory, ILogger<TenantScopeRunner> logger) : ITenantScopeRunner
{
    public async Task ForEachTenantAsync(
        Func<IServiceProvider, Tenant, CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        List<Tenant> tenants;
        await using (var listScope = scopeFactory.CreateAsyncScope())
        {
            tenants = await listScope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants
                .AsNoTracking()
                .Where(t => t.IsActive)
                .OrderBy(t => t.Slug)
                .ToListAsync(cancellationToken);
        }

        foreach (var tenant in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenant.Id);
            try
            {
                await action(scope.ServiceProvider, tenant, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One salon's failure must not stop the others; the caller sees it in the log and the task still completes.
                logger.LogError(ex, "Per-tenant work failed for tenant {Slug}", tenant.Slug);
            }
        }
    }

    public async Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = scopeFactory.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenantId);
        await action(scope.ServiceProvider, cancellationToken);
    }
}