using Salon.Core.Tenancy;

namespace Salon.Services.Tenancy;

/// <summary>
/// Runs work once per active tenant, each in a fresh DI scope with the tenant context already set, so the EF filter and
/// Postgres row-level security see the right tenant. The only sanctioned way for jobs to touch tenant data.
/// </summary>
public interface ITenantScopeRunner
{
    Task ForEachTenantAsync(Func<IServiceProvider, Tenant, CancellationToken, Task> action, CancellationToken cancellationToken = default);

    Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> action, CancellationToken cancellationToken = default);
}