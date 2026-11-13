using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Catalog;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tenancy;

/// <summary>Data created in tenant A is invisible and unmodifiable from tenant B, and nothing is visible without a tenant.</summary>
[Collection(SharedDatabase.Name)]
public class TenantIsolationTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("salon-a");
        _tenantB = await factory.CreateTenantAsync("salon-b", "pl-PL", "PLN");
        await AddServiceAsync(_tenantA.Id, "Cut A");
        await AddServiceAsync(_tenantB.Id, "Strzyżenie B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Each_tenant_sees_only_its_own_services()
    {
        (await NamesAsync(_tenantA.Id)).ShouldBe(["Cut A"]);
        (await NamesAsync(_tenantB.Id)).ShouldBe(["Strzyżenie B"]);
    }

    [Fact]
    public async Task Without_a_tenant_nothing_is_visible()
    {
        (await NamesAsync(null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Find_by_id_across_tenants_returns_nothing()
    {
        Guid idA;
        await using (var scopeA = factory.CreateTenantScope(_tenantA.Id))
        {
            idA = await scopeA.ServiceProvider.GetRequiredService<SalonDbContext>().Services.Select(s => s.Id).SingleAsync();
        }

        await using var scopeB = factory.CreateTenantScope(_tenantB.Id);
        var db = scopeB.ServiceProvider.GetRequiredService<SalonDbContext>();

        (await db.Services.FirstOrDefaultAsync(s => s.Id == idA)).ShouldBeNull();
    }

    [Fact]
    public async Task New_rows_get_the_current_tenant_automatically()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = NewService("Colour A");

        db.Services.Add(service);
        await db.SaveChangesAsync();

        service.TenantId.ShouldBe(_tenantA.Id);
    }

    [Fact]
    public async Task Writing_a_row_for_another_tenant_is_refused()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = NewService("Sneaky");
        service.TenantId = _tenantB.Id;
        db.Services.Add(service);

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Writing_without_a_tenant_is_refused()
    {
        await using var scope = factory.CreateTenantScope(null);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(NewService("Orphan"));

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Moving_a_row_between_tenants_is_refused()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var service = await db.Services.SingleAsync();
        service.TenantId = _tenantB.Id;

        await Should.ThrowAsync<TenantViolationException>(() => db.SaveChangesAsync());
    }

    private static Service NewService(string name) => new()
    {
        Name = name,
        Duration = Duration.FromMinutes(45),
        Price = 35m,
        Currency = "GBP",
    };

    private async Task AddServiceAsync(Guid tenantId, string name)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(NewService(name));
        await db.SaveChangesAsync();
    }

    private async Task<List<string>> NamesAsync(Guid? tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        return await db.Services.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync();
    }
}