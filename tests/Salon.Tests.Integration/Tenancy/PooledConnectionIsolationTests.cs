using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tenancy;

/// <summary>
/// The EF path end to end as the app role: interceptor sets app.tenant_id on a pooled connection, the connection returns
/// to the pool, the next scope reuses it for another tenant. Proves the pool reset ADR 0002 relies on, with the EF filter
/// deliberately ignored so only Postgres row-level security stands between the tenants.
/// </summary>
[Collection(SharedDatabase.Name)]
public class PooledConnectionIsolationTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("pool-a");
        _tenantB = await factory.CreateTenantAsync("pool-b");
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(new Service { Name = "Only A", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" });
        await db.SaveChangesAsync();
        await AppRole.EnsureAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_pooled_connection_reused_by_another_tenant_does_not_keep_the_previous_tenant()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = new DbContextOptionsBuilder<SalonDbContext>()
            .UseNpgsql(AppRole.ConnectionString, npgsql => npgsql.UseNodaTime())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantConnectionInterceptor(tenantContext))
            .Options;

        tenantContext.SetTenant(_tenantA.Id);
        await using (var dbA = new SalonDbContext(options, tenantContext))
        {
            (await dbA.Services.IgnoreQueryFilters().CountAsync()).ShouldBe(1);
        }

        tenantContext.SetTenant(_tenantB.Id);
        await using (var dbB = new SalonDbContext(options, tenantContext))
        {
            (await dbB.Services.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
        }

        tenantContext.SetTenant(null);
        await using var dbNone = new SalonDbContext(options, tenantContext);
        (await dbNone.Services.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    private sealed class SwitchableTenantContext : IMutableTenantContext
    {
        public Guid? TenantId { get; private set; }

        public void SetTenant(Guid? tenantId) => TenantId = tenantId;
    }
}