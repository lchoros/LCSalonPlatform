using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Npgsql;

using Respawn;

using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Caching;
using Salon.Services.Events;
using Salon.Web.Framework.Pipeline;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// Hosts Salon.Web in-process against the local test database (salon_test), migrates it once, and resets it with
/// Respawn on <see cref="ResetDatabaseAsync"/>. Tenant helpers open a scope with a given tenant, exactly like a request.
/// </summary>
public sealed class SalonWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private Respawner? _respawner;

    public static string TestConnectionString
        => Environment.GetEnvironmentVariable("ConnectionStrings__Test")
           ?? "Host=localhost;Port=5432;Database=salon_test;Username=postgres;Password=postgres";

    public SalonWebApplicationFactory()
    {
        // Environment variables are the one configuration source that reliably reaches WebApplication.CreateBuilder.
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", TestConnectionString);
        Environment.SetEnvironmentVariable("Database__MigrateOnStartup", "false");
        Environment.SetEnvironmentVariable("Jobs__Enabled", "false");
        // Flagged slices are exercised with their flag on; a test that needs it off overrides it per host.
        Environment.SetEnvironmentVariable("Features__CatalogAdmin", "true");
    }

    public async Task InitializeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(TestConnectionString);
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["salon"],
            TablesToIgnore = [new Respawn.Graph.Table("salon", "__EFMigrationsHistory")],
        });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(TestConnectionString);
        await connection.OpenAsync();
        await _respawner!.ResetAsync(connection);

        // Tenant rows are cached by slug and id; test classes reuse slugs, so a reset must also forget the old ids.
        await Services.GetRequiredService<IStaticCacheManager>().ClearAsync();
    }

    /// <summary>Opens a DI scope whose tenant context is already set, as the middleware would for a request.</summary>
    public AsyncServiceScope CreateTenantScope(Guid? tenantId)
    {
        var scope = Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IMutableTenantContext>().SetTenant(tenantId);
        return scope;
    }

    public async Task<Tenant> CreateTenantAsync(string slug, string culture = "en-GB", string currency = "GBP", string? name = null)
    {
        await using var scope = CreateTenantScope(null);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var tenant = new Tenant
        {
            Slug = slug,
            Name = name ?? slug,
            TimeZoneId = culture == "pl-PL" ? "Europe/Warsaw" : "Europe/London",
            Culture = culture,
            Currency = currency,
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
        };
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Test-only events, consumers and request handlers (Events/OutboxProbe.cs) join the host's own registrations.
        var testAssembly = typeof(SalonWebApplicationFactory).Assembly;
        builder.ConfigureServices(services => services.AddSalonEvents(testAssembly).AddSalonPipeline(testAssembly));
    }
}