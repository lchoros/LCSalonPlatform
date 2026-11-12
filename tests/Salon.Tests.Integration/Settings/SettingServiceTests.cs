using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Settings;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Settings;

/// <summary>Typed settings are stored one row per property, per tenant, and never cross tenants.</summary>
[Collection(SharedDatabase.Name)]
public class SettingServiceTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("settings-a");
        _tenantB = await factory.CreateTenantAsync("settings-b", "pl-PL", "PLN");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Load_with_no_rows_returns_the_class_defaults()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);

        var settings = await Service(scope).LoadAsync<BookingSettings>();

        settings.MinNoticeMinutes.ShouldBe(60);
        settings.MaxAdvanceDays.ShouldBe(60);
        settings.RequireSmsVerification.ShouldBeTrue();
        settings.DefaultDepositPercent.ShouldBe(0m);
    }

    [Fact]
    public async Task Save_then_load_in_a_new_scope_returns_the_saved_values()
    {
        await using (var scope = factory.CreateTenantScope(_tenantA.Id))
        {
            // Warm the cache first so the test also proves Save invalidates it.
            await Service(scope).LoadAsync<BookingSettings>();
            await Service(scope).SaveAsync(new BookingSettings
            {
                MinNoticeMinutes = 120,
                MaxAdvanceDays = 30,
                RequireSmsVerification = false,
                DefaultDepositPercent = 12.5m,
            });
        }

        await using var fresh = factory.CreateTenantScope(_tenantA.Id);
        var loaded = await Service(fresh).LoadAsync<BookingSettings>();

        loaded.MinNoticeMinutes.ShouldBe(120);
        loaded.MaxAdvanceDays.ShouldBe(30);
        loaded.RequireSmsVerification.ShouldBeFalse();
        loaded.DefaultDepositPercent.ShouldBe(12.5m);
    }

    [Fact]
    public async Task Save_writes_one_row_per_property_and_saving_again_updates_in_place()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        await Service(scope).SaveAsync(new BookingSettings { MinNoticeMinutes = 10 });
        await Service(scope).SaveAsync(new BookingSettings { MinNoticeMinutes = 20 });

        var rows = await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Settings
            .OrderBy(s => s.Name).ToListAsync();

        rows.Count.ShouldBe(typeof(BookingSettings).GetProperties().Length); // one row per public property
        rows.ShouldAllBe(r => r.TenantId == _tenantA.Id);
        rows.Single(r => r.Name == "BookingSettings.MinNoticeMinutes").Value.ShouldBe("20");
        rows.Single(r => r.Name == "BookingSettings.DefaultDepositPercent").Value.ShouldBe("0");
    }

    [Fact]
    public async Task Settings_saved_by_tenant_A_are_not_visible_to_tenant_B()
    {
        await using (var scopeA = factory.CreateTenantScope(_tenantA.Id))
        {
            await Service(scopeA).SaveAsync(new BookingSettings { MinNoticeMinutes = 240 });
        }

        await using var scopeB = factory.CreateTenantScope(_tenantB.Id);
        var loadedB = await Service(scopeB).LoadAsync<BookingSettings>();
        var rawB = await Service(scopeB).GetRawAsync("BookingSettings.MinNoticeMinutes");
        var rowsB = await scopeB.ServiceProvider.GetRequiredService<SalonDbContext>().Settings.CountAsync();

        loadedB.MinNoticeMinutes.ShouldBe(60);
        rawB.ShouldBeNull();
        rowsB.ShouldBe(0);
    }

    [Fact]
    public async Task Each_tenant_keeps_its_own_value_for_the_same_setting()
    {
        await using (var scopeA = factory.CreateTenantScope(_tenantA.Id))
        {
            await Service(scopeA).SetRawAsync("BookingSettings.MaxAdvanceDays", "7");
        }

        await using (var scopeB = factory.CreateTenantScope(_tenantB.Id))
        {
            await Service(scopeB).SetRawAsync("BookingSettings.MaxAdvanceDays", "90");
        }

        await using var checkA = factory.CreateTenantScope(_tenantA.Id);
        await using var checkB = factory.CreateTenantScope(_tenantB.Id);
        (await Service(checkA).LoadAsync<BookingSettings>()).MaxAdvanceDays.ShouldBe(7);
        (await Service(checkB).LoadAsync<BookingSettings>()).MaxAdvanceDays.ShouldBe(90);
    }

    [Fact]
    public async Task Raw_get_and_set_round_trip_and_null_clears_the_value()
    {
        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var service = Service(scope);

        (await service.GetRawAsync("Custom.Flag")).ShouldBeNull();
        await service.SetRawAsync("Custom.Flag", "on");
        (await service.GetRawAsync("Custom.Flag")).ShouldBe("on");
        await service.SetRawAsync("Custom.Flag", null);
        (await service.GetRawAsync("Custom.Flag")).ShouldBeNull();
    }

    [Fact]
    public async Task Without_a_tenant_every_operation_throws_instead_of_silently_returning_defaults()
    {
        await using var scope = factory.CreateTenantScope(null);
        var service = Service(scope);

        await Should.ThrowAsync<TenantViolationException>(() => service.LoadAsync<BookingSettings>());
        await Should.ThrowAsync<TenantViolationException>(() => service.SaveAsync(new BookingSettings()));
        await Should.ThrowAsync<TenantViolationException>(() => service.GetRawAsync("BookingSettings.MinNoticeMinutes"));
        await Should.ThrowAsync<TenantViolationException>(() => service.SetRawAsync("BookingSettings.MinNoticeMinutes", "1"));
    }

    private static ISettingService Service(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ISettingService>();
}