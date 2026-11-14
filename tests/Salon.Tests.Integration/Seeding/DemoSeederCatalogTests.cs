using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>
/// The demo catalogue depth of ADR 0013: three categories with the six services filed under them and given buffers and
/// processing time on a fresh salon; the same added once to an existing demo salon that has no categories; a salon that
/// already has any category left alone.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederCatalogTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_fresh_demo_salon_gets_three_categories_and_timed_services()
    {
        (await SeedAsync()).ShouldBeTrue();
        var tenantId = await TenantIdAsync();

        var categories = await CategoriesAsync(tenantId);
        categories.Select(c => (c.Name, c.SortOrder, c.IsActive)).ShouldBe([("Hair", 1, true), ("Barbering", 2, true), ("Nails", 3, true)]);
        categories.ShouldAllBe(c => c.TenantId == tenantId);
        var byName = categories.ToDictionary(c => c.Name);

        var services = await ServicesAsync(tenantId);
        services.Select(s => (s.Name, Category: byName.Single(c => c.Value.Id == s.CategoryId).Key, s.BufferAfterMinutes, s.ProcessingMinutes, s.ProcessingStartsAfterMinutes)).ShouldBe(
        [
            ("Cut & blow-dry", "Hair", 5, 0, 0),
            ("Colour (roots)", "Hair", 15, 30, 20),
            ("Balayage", "Hair", 15, 45, 45),
            ("Beard trim", "Barbering", 5, 0, 0),
            ("Manicure", "Nails", 5, 0, 0),
            ("Pedicure", "Nails", 5, 0, 0),
        ]);
        services.ShouldAllBe(s => ServiceRules.ProcessingFitsInside((int)s.Duration.TotalMinutes, s.ProcessingMinutes, s.ProcessingStartsAfterMinutes));
    }

    [Fact]
    public async Task An_existing_demo_salon_without_categories_receives_them_once_and_its_services_are_filed_again()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.ServiceCategories.RemoveRange(await db.ServiceCategories.ToListAsync());
            var colour = await db.Services.SingleAsync(s => s.Name == "Colour (roots)");
            colour.BufferAfterMinutes = 0;
            colour.ProcessingMinutes = 0;
            colour.ProcessingStartsAfterMinutes = 0;
            db.Services.Remove(await db.Services.SingleAsync(s => s.Name == "Pedicure"));
            await db.SaveChangesAsync();
        }
        (await ServicesAsync(tenantId)).ShouldAllBe(s => s.CategoryId == null, "deleting a category leaves its services uncategorised");

        (await SeedAsync()).ShouldBeFalse("the tenant already existed");

        var categories = await CategoriesAsync(tenantId);
        categories.Select(c => c.Name).ShouldBe(["Hair", "Barbering", "Nails"]);
        var services = await ServicesAsync(tenantId);
        services.Count.ShouldBe(5, "the catalogue itself is never re-seeded");
        services.ShouldAllBe(s => s.CategoryId != null);
        var colourAgain = services.Single(s => s.Name == "Colour (roots)");
        (colourAgain.BufferAfterMinutes, colourAgain.ProcessingMinutes, colourAgain.ProcessingStartsAfterMinutes).ShouldBe((15, 30, 20));

        (await SeedAsync()).ShouldBeFalse();
        (await CategoriesAsync(tenantId)).Count.ShouldBe(3, "seeding again adds nothing");
    }

    [Fact]
    public async Task A_salon_that_already_has_a_category_is_left_alone()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.ServiceCategories.RemoveRange(await db.ServiceCategories.Where(c => c.Name != "Barbering").ToListAsync());
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        (await CategoriesAsync(tenantId)).Select(c => c.Name).ShouldBe(["Barbering"]);
        var services = await ServicesAsync(tenantId);
        services.Single(s => s.Name == "Cut & blow-dry").CategoryId.ShouldBeNull();
        services.Single(s => s.Name == "Beard trim").CategoryId.ShouldNotBeNull();
    }

    private async Task<bool> SeedAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DemoSeeder>().SeedAsync();
    }

    private async Task<Guid> TenantIdAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Tenants.AsNoTracking().SingleAsync(t => t.Slug == DemoSeeder.Slug)).Id;
    }

    private async Task<List<ServiceCategory>> CategoriesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().ServiceCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
    }

    private async Task<List<Service>> ServicesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
    }
}