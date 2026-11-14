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
    public async Task A_fresh_demo_salon_gets_the_pilot_salons_categories_services_and_variants()
    {
        (await SeedAsync()).ShouldBeTrue();
        var tenantId = await TenantIdAsync();

        var categories = await CategoriesAsync(tenantId);
        categories.Select(c => (c.Name, c.SortOrder, c.IsActive)).ShouldBe([("Hair", 1, true), ("Brows", 2, true), ("Eyelashes", 3, true), ("Waxing", 4, true), ("Beauty", 5, true)]);
        categories.ShouldAllBe(c => c.TenantId == tenantId);
        var byName = categories.ToDictionary(c => c.Name);

        var services = await ServicesAsync(tenantId);
        services.Count.ShouldBe(66);
        services.Select(s => s.Name).Distinct(StringComparer.Ordinal).Count().ShouldBe(66);
        services.GroupBy(s => byName.Single(c => c.Value.Id == s.CategoryId).Key).Select(g => (g.Key, Count: g.Count())).ShouldBe(
            [("Hair", 45), ("Brows", 6), ("Eyelashes", 8), ("Waxing", 3), ("Beauty", 4)], ignoreOrder: true);
        services.ShouldAllBe(s => ServiceRules.IsOnStep((int)s.Duration.TotalMinutes) && ServiceRules.IsDurationInRange((int)s.Duration.TotalMinutes));
        services.ShouldAllBe(s => ServiceRules.VariantIsComplete(s.VariantGroup, s.VariantLabel));

        // Keratin is the one service whose variants the price list names.
        var keratin = services.Where(s => s.VariantGroup == "HAIR WOMEN 14: Keratin").ToList();
        keratin.Select(s => (s.VariantLabel, s.Price)).ShouldBe(
        [
            ("Amazon - Grape, Apple, Botox - Medium hair", 75m),
            ("Amazon - Grape, Apple, Botox - Long hair", 85m),
            ("Amazon - AntiFrizz - Medium hair", 75m),
            ("Amazon - AntiFrizz - Long hair", 85m),
            ("CocoChoco - Long hair", 75m),
            ("CocoChoco - Medium hair", 70m),
        ]);
        keratin[5].Name.ShouldBe("HAIR WOMEN 14: Keratin \u00B7 CocoChoco - Medium hair", "the full name stands on its own in a message or on the calendar");

        // A price range without named variants is two options at its ends, for the salon to rename.
        services.Where(s => s.VariantGroup == "HAIR WOMEN 01: Full Colour").Select(s => (s.VariantLabel, s.Price)).ShouldBe([("Option 1", 65m), ("Option 2", 85m)]);
        services.Count(s => s.VariantGroup is null).ShouldBe(32);

        var remover = services.Single(s => s.Name == "HAIR WOMEN 05: Remove Colour");
        (remover.BufferAfterMinutes, remover.ProcessingMinutes, remover.ProcessingStartsAfterMinutes).ShouldBe((10, 20, 15));
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
            var colour = await db.Services.SingleAsync(s => s.Name == "HAIR WOMEN 05: Remove Colour");
            colour.BufferAfterMinutes = 0;
            colour.ProcessingMinutes = 0;
            colour.ProcessingStartsAfterMinutes = 0;
            db.Services.Remove(await db.Services.SingleAsync(s => s.Name == "WAXING03: Ear Wax"));
            await db.SaveChangesAsync();
        }
        (await ServicesAsync(tenantId)).ShouldAllBe(s => s.CategoryId == null, "deleting a category leaves its services uncategorised");

        (await SeedAsync()).ShouldBeFalse("the tenant already existed");

        var categories = await CategoriesAsync(tenantId);
        categories.Select(c => c.Name).ShouldBe(["Hair", "Brows", "Eyelashes", "Waxing", "Beauty"]);
        var services = await ServicesAsync(tenantId);
        services.Count.ShouldBe(65, "the catalogue itself is never re-seeded");
        services.ShouldAllBe(s => s.CategoryId != null);
        var colourAgain = services.Single(s => s.Name == "HAIR WOMEN 05: Remove Colour");
        (colourAgain.BufferAfterMinutes, colourAgain.ProcessingMinutes, colourAgain.ProcessingStartsAfterMinutes).ShouldBe((10, 20, 15));

        (await SeedAsync()).ShouldBeFalse();
        (await CategoriesAsync(tenantId)).Count.ShouldBe(5, "seeding again adds nothing");
    }

    [Fact]
    public async Task A_salon_that_already_has_a_category_is_left_alone()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.ServiceCategories.RemoveRange(await db.ServiceCategories.Where(c => c.Name != "Brows").ToListAsync());
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        (await CategoriesAsync(tenantId)).Select(c => c.Name).ShouldBe(["Brows"]);
        var services = await ServicesAsync(tenantId);
        services.Single(s => s.Name == "HAIR WOMEN 11: Fringe").CategoryId.ShouldBeNull();
        services.Single(s => s.Name == "BROWS04: Brows Lamination").CategoryId.ShouldNotBeNull();
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