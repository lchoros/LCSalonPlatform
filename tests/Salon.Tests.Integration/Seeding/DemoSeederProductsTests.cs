using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Products;
using Salon.Data;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>
/// The demo shelf of ADR 0036: seven products on a fresh salon, each started with a count so it has a history, the
/// same seven added once to an existing demo salon that has none, and nothing touched when products already exist.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederProductsTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_fresh_demo_salon_gets_a_small_shelf_with_a_first_count_each()
    {
        (await SeedAsync()).ShouldBeTrue();
        var tenantId = await TenantIdAsync();

        var (products, lines) = await ShelfAsync(tenantId);

        products.Count.ShouldBe(7);
        products.ShouldAllBe(p => p.TenantId == tenantId && p.IsActive && p.Currency == "GBP" && p.LowStockAt != null);
        products.Count(p => p.RetailPrice is not null).ShouldBe(4, "four are sold to clients");
        products.Count(p => StockRules.IsLow(p.StockOnHand, p.LowStockAt)).ShouldBe(2, "two show what low looks like");
        lines.Count.ShouldBe(7);
        lines.ShouldAllBe(l => l.Kind == StockMovementKind.Counted && l.Quantity > 0 && l.ByUserId == null);
        foreach (var product in products)
        {
            lines.Single(l => l.ProductId == product.Id).OnHandAfter.ShouldBe(product.StockOnHand, product.Name);
        }
    }

    [Fact]
    public async Task An_existing_demo_salon_gets_the_shelf_once_and_keeps_products_it_already_has()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();

        (await SeedAsync()).ShouldBeFalse("the tenant already existed");
        (await ShelfAsync(tenantId)).Products.Count.ShouldBe(7, "a second run adds nothing");

        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            await db.StockMovements.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            db.Products.Add(new Product { Name = "The salon's own wax", Currency = "GBP" });
            await db.SaveChangesAsync();
        }

        await SeedAsync();

        (await ShelfAsync(tenantId)).Products.ShouldHaveSingleItem().Name.ShouldBe("The salon's own wax");
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

    private async Task<(List<Product> Products, List<StockMovement> Lines)> ShelfAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        return (await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(), await db.StockMovements.AsNoTracking().ToListAsync());
    }
}