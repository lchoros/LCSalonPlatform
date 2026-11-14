using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Catalog;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Identity;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>
/// The Development-only demo seed: one studio-20 tenant with six GBP services and, when Demo:OwnerPassword is set, two
/// logins; created once and left alone afterwards.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Seeding_twice_yields_one_tenant_and_its_catalogue_once()
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
        services.Count.ShouldBe(66);
        services.ShouldAllBe(s => s.TenantId == tenant.Id);
        services.ShouldAllBe(s => s.Currency == "GBP");
        services.ShouldAllBe(s => s.IsActive);
        services.ShouldAllBe(s => s.Price > 0m && s.Duration.TotalMinutes >= 5);
        services.Select(s => s.SortOrder).ShouldBe(Enumerable.Range(1, 66));
        services[2].Name.ShouldBe("HAIR KIDS 22: Girl's Haircut");
        services[^1].Name.ShouldBe("BEAUTY04: Microdermabrasion");
    }

    [Fact]
    public async Task An_existing_tenant_is_left_alone_even_when_its_catalogue_changed()
    {
        await SeedAsync();
        var tenantId = (await TenantsAsync()).Single().Id;
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Services.Remove(await db.Services.SingleAsync(s => s.Name == "WAXING03: Ear Wax"));
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        (await ServicesAsync(tenantId)).Count.ShouldBe(65);
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
        html.ShouldContain("HAIR WOMEN 13: Olaplex");
        html.ShouldContain("£20.00");
    }

    [Fact]
    public async Task Without_a_demo_password_the_salon_has_no_logins()
    {
        await SeedAsync();
        var tenantId = (await TenantsAsync()).Single().Id;

        (await UsersAsync(tenantId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_existing_demo_salon_without_logins_gets_them_once_the_password_is_configured()
    {
        await SeedAsync();
        var tenantId = (await TenantsAsync()).Single().Id;
        (await UsersAsync(tenantId)).ShouldBeEmpty();

        using var configured = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:OwnerPassword"] = "Studio20!test" })));
        await using (var scope = configured.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync()).ShouldBeFalse();
            (await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync()).ShouldBeFalse();
        }

        (await TenantsAsync()).Count.ShouldBe(1);
        (await ServicesAsync(tenantId)).Count.ShouldBe(66);
        (await UsersAsync(tenantId)).Select(u => u.Email).ShouldBe(["owner@studio-20.local", "desk@studio-20.local"], ignoreOrder: true);
    }

    [Fact]
    public async Task With_a_demo_password_an_owner_and_a_receptionist_can_sign_in()
    {
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration(c =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:OwnerPassword"] = "Studio20!test" })));
        await using (var scope = configured.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync()).ShouldBeTrue();
        }

        var tenantId = (await TenantsAsync()).Single().Id;
        var users = await UsersAsync(tenantId);
        users.Select(u => (u.Email, u.Role)).ShouldBe([("owner@studio-20.local", Role.Owner), ("desk@studio-20.local", Role.Receptionist)], ignoreOrder: true);
        users.ShouldAllBe(u => u.TenantId == tenantId && u.IsActive);

        await using var tenantScope = factory.CreateTenantScope(tenantId);
        var signIn = tenantScope.ServiceProvider.GetRequiredService<ISignInService>();
        (await signIn.AttemptAsync("owner@studio-20.local", "Studio20!test")).Succeeded.ShouldBeTrue();
        (await signIn.AttemptAsync("desk@studio-20.local", "Studio20!test")).Succeeded.ShouldBeTrue();
        (await signIn.AttemptAsync("desk@studio-20.local", "Studio20!dev")).Succeeded.ShouldBeFalse();
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

    private async Task<List<User>> UsersAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
    }
}