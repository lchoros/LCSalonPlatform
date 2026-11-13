using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Salon.Core.Staff;
using Salon.Data;
using Salon.Services.Seeding;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Seeding;

/// <summary>
/// The demo staff of ADR 0010: three people with the stated rota on a fresh salon, the same three added once to an
/// existing demo salon that has none, and nothing touched when staff already exist.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DemoSeederStaffTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_fresh_demo_salon_gets_three_staff_with_their_weekly_hours()
    {
        (await SeedAsync()).ShouldBeTrue();
        var tenantId = await TenantIdAsync();

        var staff = await StaffAsync(tenantId);
        staff.Select(s => s.DisplayName).ShouldBe(["Anna Kowalska", "Tom Reed", "Priya Shah"]);
        staff.Select(s => s.SortOrder).ShouldBe([1, 2, 3]);
        staff.ShouldAllBe(s => s.TenantId == tenantId && s.IsActive && s.IsBookable && s.UserId == null);
        staff.Select(s => s.Colour).Distinct().Count().ShouldBe(3);
        staff.ShouldAllBe(s => StaffMember.IsValidColour(s.Colour));
        staff.ShouldAllBe(s => s.CreatedAt > Instant.MinValue);

        var hours = await HoursAsync(tenantId);
        hours.ShouldAllBe(h => h.TenantId == tenantId);

        var anna = Rota(hours, staff[0].Id);
        anna.Keys.ShouldBe([IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday, IsoDayOfWeek.Saturday]);
        anna.Values.ShouldAllBe(ranges => ranges.Count == 1 && ranges[0] == Range(10, 18));

        var tom = Rota(hours, staff[1].Id);
        tom.Keys.ShouldBe([IsoDayOfWeek.Monday, IsoDayOfWeek.Tuesday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Thursday, IsoDayOfWeek.Friday]);
        tom.Values.ShouldAllBe(ranges => ranges.Count == 2 && ranges[0] == Range(9, 13) && ranges[1] == Range(14, 17));

        var priya = Rota(hours, staff[2].Id);
        priya.Keys.ShouldBe([IsoDayOfWeek.Monday, IsoDayOfWeek.Wednesday, IsoDayOfWeek.Friday]);
        priya.Values.ShouldAllBe(ranges => ranges.Count == 1 && ranges[0] == Range(9, 15));

        hours.Count.ShouldBe(5 + 10 + 3);
    }

    [Fact]
    public async Task An_existing_demo_salon_without_staff_receives_the_three_once_and_keeps_its_catalogue()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Appointments.RemoveRange(await db.Appointments.ToListAsync()); // appointments hold their staff member (Restrict)
            db.StaffMembers.RemoveRange(await db.StaffMembers.ToListAsync());
            db.Services.Remove(await db.Services.SingleAsync(s => s.Name == "Pedicure"));
            await db.SaveChangesAsync();
        }
        (await HoursAsync(tenantId)).ShouldBeEmpty("deleting a person cascades to their rota");

        (await SeedAsync()).ShouldBeFalse("the tenant already existed");

        (await StaffAsync(tenantId)).Select(s => s.DisplayName).ShouldBe(["Anna Kowalska", "Tom Reed", "Priya Shah"]);
        (await HoursAsync(tenantId)).Count.ShouldBe(18);
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            (await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.CountAsync()).ShouldBe(5, "the catalogue is never re-seeded");
        }

        (await SeedAsync()).ShouldBeFalse();
        (await StaffAsync(tenantId)).Count.ShouldBe(3, "seeding again adds nobody");
    }

    [Fact]
    public async Task A_salon_that_already_has_staff_is_left_alone()
    {
        await SeedAsync();
        var tenantId = await TenantIdAsync();
        await using (var scope = factory.CreateTenantScope(tenantId))
        {
            var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
            db.Appointments.RemoveRange(await db.Appointments.ToListAsync());
            db.StaffMembers.RemoveRange(await db.StaffMembers.Where(s => s.DisplayName != "Tom Reed").ToListAsync());
            await db.SaveChangesAsync();
        }

        (await SeedAsync()).ShouldBeFalse();

        (await StaffAsync(tenantId)).Select(s => s.DisplayName).ShouldBe(["Tom Reed"]);
        (await HoursAsync(tenantId)).Count.ShouldBe(10);
    }

    private static TimeRange Range(int fromHour, int toHour) => new(new LocalTime(fromHour, 0), new LocalTime(toHour, 0));

    private static SortedDictionary<IsoDayOfWeek, List<TimeRange>> Rota(IEnumerable<WorkingHours> hours, Guid staffId)
        => new(hours.Where(h => h.StaffMemberId == staffId)
            .GroupBy(h => h.Day)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.Start).Select(h => h.Range).ToList()));

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

    private async Task<List<StaffMember>> StaffAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().StaffMembers.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync();
    }

    private async Task<List<WorkingHours>> HoursAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().WorkingHours.AsNoTracking().ToListAsync();
    }
}