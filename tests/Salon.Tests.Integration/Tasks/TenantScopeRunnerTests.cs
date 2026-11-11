using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tasks;

[Collection(SharedDatabase.Name)]
public class TenantScopeRunnerTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Runs_once_per_active_tenant_with_that_tenant_in_scope()
    {
        var a = await factory.CreateTenantAsync("scope-a");
        var b = await factory.CreateTenantAsync("scope-b");
        var closed = await factory.CreateTenantAsync("scope-closed");
        await using (var scope = factory.CreateTenantScope(null))
        {
            var db = scope.ServiceProvider.GetRequiredService<Salon.Data.SalonDbContext>();
            db.Tenants.Attach(closed);
            closed.IsActive = false;
            await db.SaveChangesAsync();
        }

        var seen = new List<(string Slug, Guid? ContextTenant)>();
        var runner = factory.Services.GetRequiredService<ITenantScopeRunner>();

        await runner.ForEachTenantAsync((services, tenant, _) =>
        {
            seen.Add((tenant.Slug, services.GetRequiredService<ITenantContext>().TenantId));
            return Task.CompletedTask;
        });

        seen.ShouldBe([("scope-a", a.Id), ("scope-b", b.Id)]);
    }

    [Fact]
    public async Task One_failing_tenant_does_not_stop_the_others()
    {
        await factory.CreateTenantAsync("fail-a");
        await factory.CreateTenantAsync("fail-b");
        var runner = factory.Services.GetRequiredService<ITenantScopeRunner>();
        var visited = new List<string>();

        await runner.ForEachTenantAsync((_, tenant, _) =>
        {
            visited.Add(tenant.Slug);
            return tenant.Slug == "fail-a" ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        });

        visited.ShouldBe(["fail-a", "fail-b"]);
    }
}