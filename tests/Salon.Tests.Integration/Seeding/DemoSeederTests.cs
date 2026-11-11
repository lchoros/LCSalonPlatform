using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Catalog;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>The Development-only demo seed: one studio-20 tenant with six GBP services, created once and left alone afterwards.</summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeding_twice_yields_one_tenant_and_six_services_owned_by_it()
    {
        (await SeedAsync()).ShouldBeTrue();
        (await SeedAsync()).ShouldBeFalse();

        var tenant = (await TenantsAsync()).ShouldHaveSingleItem();
        tenant.Slug.ShouldBe("studio-20");
        tenant.Name.ShouldBe("Studio 20");
        tenant.TimeZoneId.ShouldBe("Europe/London");
        tenant.Culture.ShouldBe("en-GB");
        tenant.Currency.ShouldBe("GBP");
        tenant.IsActive.ShouldBeTrue();

        var services = await ServicesAsync(tenant.Id);
        services.Count.ShouldBe(6);
        services.ShouldAllBe(s => s.TenantId == tenant.Id);
        services.ShouldAllBe(s => s.Currency == "GBP");
        services.ShouldAllBe(s => s.IsActive);
        services.ShouldAllBe(s => s.Price > 0m && s.Duration.TotalMinutes >= 5);
        services.Select(s => s.SortOrder).ShouldBe([1, 2, 3, 4, 5, 6]);
        services.Select(s => s.Name).ShouldBe(["Cut & blow-dry", "Colour (roots)", "Balayage", "Beard trim", "Manicure", "Pedicure"]);
    }

    [Fact]
    public async Task An_existing_tenant_is_left_alone_even_when_its_catalogue_changed()
    {
        await SeedAsync();
        var tenantId = (await TenantsAsync()).Single().Id;
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Services.Remove(await db.Services.SingleAsync(s => s.Name == "Pedicure"));
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        (await ServicesAsync(tenantId)).Count.ShouldBe(5);
    }

    [Fact]
    public async Task The_demo_salon_answers_on_its_subdomain_after_seeding()
    {
        await SeedAsync();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "studio-20.localhost";

        var response = await client.GetAsync(new Uri("/services", UriKind.Relative));

        response.IsSuccessStatusCode.ShouldBeTrue();
        var html = await response.Content.ReadAsStringAsync();
        html.ShouldContain("Studio 20");
        html.ShouldContain("Balayage");
        html.ShouldContain("£140.00");
    }

    private async Task<bool> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
    }

    private async Task<List<Tenant>> TenantsAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants.AsNoTracking().ToListAsync();
    }

    private async Task<List<Service>> ServicesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
    }
}