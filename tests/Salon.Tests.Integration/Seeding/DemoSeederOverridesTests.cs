using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Data;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>
/// The demo staff overrides of ADR 0018: Anna Kowalska is slower and dearer on "Cut &amp; blow-dry" and Priya Shah (the
/// nail technician) does not offer it; added once on a fresh salon, once to an existing demo salon that has none, and never
/// twice.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederOverridesTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_fresh_demo_salon_gets_one_slower_dearer_stylist_and_one_person_who_does_not_offer_a_service()
    {
        (await SeedAsync()).ShouldBeTrue();
        var tenantId = await TenantIdAsync();

        var rows = await OverridesAsync(tenantId);
        rows.Count.ShouldBe(2);
        rows.ShouldAllBe(o => o.TenantId == tenantId);
        var (anna, priya) = (rows.Single(o => o.Staff == "Anna Kowalska"), rows.Single(o => o.Staff == "Priya Shah"));
        anna.Service.ShouldBe("Cut & blow-dry");
        anna.Row.Offers.ShouldBeTrue();
        anna.Row.Duration.ShouldBe(Duration.FromMinutes(60));
        anna.Row.Price.ShouldBe(55m);
        priya.Service.ShouldBe("Cut & blow-dry");
        priya.Row.Offers.ShouldBeFalse();
        priya.Row.Duration.ShouldBeNull();
        priya.Row.Price.ShouldBeNull();
    }

    [Fact]
    public async Task An_existing_demo_salon_without_overrides_receives_them_once()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.StaffServiceOverrides.RemoveRange(await db.StaffServiceOverrides.ToListAsync());
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse("the tenant already existed");
        (await OverridesAsync(tenantId)).Count.ShouldBe(2);

        (await SeedAsync()).ShouldBeFalse();
        (await OverridesAsync(tenantId)).Count.ShouldBe(2, "seeding again adds nothing");
    }

    [Fact]
    public async Task A_salon_that_already_has_any_override_is_left_alone()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        Guid keptId;
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            var rows = await db.StaffServiceOverrides.ToListAsync();
            db.StaffServiceOverrides.Remove(rows.Single(o => !o.Offers));
            var kept = rows.Single(o => o.Offers);
            kept.Price = 70m;
            keptId = kept.Id;
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        var remaining = (await OverridesAsync(tenantId)).ShouldHaveSingleItem();
        remaining.Row.Id.ShouldBe(keptId);
        remaining.Row.Price.ShouldBe(70m);
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

    private async Task<List<(StaffServiceOverride Row, string Staff, string Service)>> OverridesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var rows = await db.StaffServiceOverrides.AsNoTracking().ToListAsync();
        var staff = await db.StaffMembers.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.DisplayName);
        var services = await db.Services.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);
        return rows.Select(r => (r, staff[r.StaffMemberId], services[r.ServiceId])).ToList();
    }
}
