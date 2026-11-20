using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Npgsql;
using Salon.Core.Catalog;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tenancy;

/// <summary>
/// The tenant setting has to reach every statement on its own (ADR 0090). Seen live on the pilot host, which puts
/// PgBouncer in front of Postgres: the first <c>/book</c> after a restart showed "not open for booking yet" although the
/// salon's services were all there. Everything here runs as the non-superuser role, so row-level security really
/// applies, and most of it behind <see cref="TransactionPoolerSimulator"/>, where session state does not follow the app.
/// </summary>
[Collection(SharedDatabase.Name)]
public class TenantSettingBehindPoolerTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("pooler-a", name: "Pooler A");
        _tenantB = await factory.CreateTenantAsync("pooler-b", name: "Pooler B");
        await AddServiceAsync(_tenantA.Id, "Cut A");
        await AddServiceAsync(_tenantA.Id, "Colour A");
        await AddServiceAsync(_tenantB.Id, "Cut B");
        await AppRole.EnsureAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task The_first_request_of_a_freshly_started_host_shows_the_booking_page_with_its_services_behind_a_pooler()
    {
        var pooler = new TransactionPoolerSimulator(leftBehind: null);
        await using var host = FreshHostAsAppRole(pooler.Interceptors);

        // The very first request this host serves: nothing is cached, so the middleware itself looks the salon up first.
        var html = await FirstPageAsync(host, "pooler-a.localhost", "/book");

        pooler.Handovers.ShouldBeGreaterThan(0, "the page must have run its statements through the simulated pooler");
        html.ShouldNotContain("data-testid=\"empty-state\"");
        html.ShouldContain("Cut A");
        html.ShouldContain("Colour A");
        html.ShouldNotContain("Cut B");
    }

    [Fact]
    public async Task The_first_request_of_a_freshly_started_host_shows_only_its_own_salon_when_the_pooler_hands_it_another_salons_session()
    {
        var pooler = new TransactionPoolerSimulator(leftBehind: _tenantB.Id);
        await using var host = FreshHostAsAppRole(pooler.Interceptors);

        var html = await FirstPageAsync(host, "pooler-a.localhost", "/book");

        html.ShouldNotContain("data-testid=\"empty-state\"");
        html.ShouldContain("Cut A");
        html.ShouldNotContain("Cut B");
    }

    /// <summary>The control: with no pooler in between, the first request was never the problem.</summary>
    [Fact]
    public async Task The_first_request_of_a_freshly_started_host_shows_the_booking_page_with_its_services_on_a_direct_connection()
    {
        await using var host = FreshHostAsAppRole();

        var html = await FirstPageAsync(host, "pooler-a.localhost", "/book");

        html.ShouldNotContain("data-testid=\"empty-state\"");
        html.ShouldContain("Cut A");
        html.ShouldContain("Colour A");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_query_sees_its_own_salons_rows_whatever_session_the_pooler_hands_it(bool anotherSalonsSession)
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext, new TransactionPoolerSimulator(anotherSalonsSession ? _tenantB.Id : null));

        tenantContext.SetTenant(_tenantA.Id);
        await using var db = new SalonDbContext(options, tenantContext);

        (await db.Services.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync()).ShouldBe(["Colour A", "Cut A"]);
        db.Services.OrderBy(s => s.Name).Select(s => s.Name).ToList().ShouldBe(["Colour A", "Cut A"], "the synchronous path carries the tenant too");
    }

    /// <summary>The EF filter is switched off on purpose, as in PooledConnectionIsolationTests: only row-level security is left.</summary>
    [Fact]
    public async Task Row_level_security_alone_keeps_to_the_current_salon_when_the_session_belongs_to_another()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext, new TransactionPoolerSimulator(_tenantB.Id));

        tenantContext.SetTenant(_tenantA.Id);
        await using var db = new SalonDbContext(options, tenantContext);

        (await db.Services.IgnoreQueryFilters().OrderBy(s => s.Name).Select(s => s.Name).ToListAsync()).ShouldBe(["Colour A", "Cut A"]);
    }

    [Fact]
    public async Task Without_a_tenant_nothing_is_visible_even_when_the_session_still_carries_a_salon()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext, new TransactionPoolerSimulator(_tenantA.Id));

        await using var db = new SalonDbContext(options, tenantContext);

        (await db.Services.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Saving_and_bulk_changes_reach_the_database_behind_a_pooler()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext, new TransactionPoolerSimulator(leftBehind: null));
        tenantContext.SetTenant(_tenantA.Id);

        await using (var db = new SalonDbContext(options, tenantContext))
        {
            // One row, no transaction of the caller's own: the save must bring the tenant along by itself.
            db.Services.Add(NewService("Perm A"));
            await db.SaveChangesAsync();

            db.Services.Add(NewService("Wash A"));
            db.SaveChanges();

            (await db.Services.Where(s => s.Name == "Perm A").ExecuteUpdateAsync(s => s.SetProperty(x => x.Price, 30m))).ShouldBe(1);
            (await db.Services.Where(s => s.Name == "Wash A").ExecuteDeleteAsync()).ShouldBe(1);
        }

        (await NamesAsync(_tenantA.Id)).ShouldBe(["Colour A", "Cut A", "Perm A"]);
        (await NamesAsync(_tenantB.Id)).ShouldBe(["Cut B"]);
    }

    [Fact]
    public async Task A_transaction_keeps_the_tenant_from_its_first_statement_to_its_last_behind_a_pooler()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext, new TransactionPoolerSimulator(_tenantB.Id));
        tenantContext.SetTenant(_tenantA.Id);

        await using (var db = new SalonDbContext(options, tenantContext))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Services.Add(NewService("Perm A"));
            db.Services.Add(NewService("Wash A"));
            await db.SaveChangesAsync();
            (await db.Services.CountAsync()).ShouldBe(4);
            (await db.Services.Where(s => s.Name == "Wash A").ExecuteDeleteAsync()).ShouldBe(1);
            await transaction.CommitAsync();
        }

        (await NamesAsync(_tenantA.Id)).ShouldBe(["Colour A", "Cut A", "Perm A"]);
        (await NamesAsync(_tenantB.Id)).ShouldBe(["Cut B"]);
    }

    /// <summary>
    /// Sign-up (ADR 0075) does this: the command's transaction opens before the new salon exists, and the salon becomes
    /// the current one halfway through. No pooler needed; a direct connection is enough.
    /// </summary>
    [Fact]
    public async Task A_tenant_set_after_the_transaction_began_owns_the_rows_written_from_then_on()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext);
        Guid newTenantId;

        await using (var db = new SalonDbContext(options, tenantContext))
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var tenant = new Tenant { Slug = "pooler-new", Name = "New", TimeZoneId = "Europe/London", Culture = "en-GB", Currency = "GBP", CreatedAt = SystemClock.Instance.GetCurrentInstant() };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            newTenantId = tenant.Id;

            tenantContext.SetTenant(tenant.Id);
            db.Services.Add(NewService("First service"));
            await db.SaveChangesAsync();
            (await db.Services.Select(s => s.Name).ToListAsync()).ShouldBe(["First service"]);
            await transaction.CommitAsync();
        }

        (await NamesAsync(newTenantId)).ShouldBe(["First service"]);
    }

    [Fact]
    public async Task A_tenant_changed_inside_a_transaction_never_shows_the_previous_tenants_rows()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext);
        tenantContext.SetTenant(_tenantA.Id);

        await using var db = new SalonDbContext(options, tenantContext);
        await using var transaction = await db.Database.BeginTransactionAsync();
        (await db.Services.IgnoreQueryFilters().CountAsync()).ShouldBe(2);

        tenantContext.SetTenant(_tenantB.Id);
        (await db.Services.IgnoreQueryFilters().Select(s => s.Name).ToListAsync()).ShouldBe(["Cut B"]);

        tenantContext.SetTenant(null);
        (await db.Services.IgnoreQueryFilters().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task A_single_value_read_carries_the_tenant_too()
    {
        var tenantContext = new SwitchableTenantContext();
        var pooler = new TransactionPoolerSimulator(_tenantB.Id);
        var options = Options(tenantContext, pooler);
        tenantContext.SetTenant(_tenantA.Id);
        await using var db = new SalonDbContext(options, tenantContext);
        var count = db.GetService<IRawSqlCommandBuilder>().Build("SELECT count(*) FROM salon.services");
        // With the command logger, as EF itself runs its commands: without one no interceptor is called at all.
        var parameters = new RelationalCommandParameterObject(db.GetService<IRelationalConnection>(), null, null, db, db.GetService<IRelationalCommandDiagnosticsLogger>());

        (await count.ExecuteScalarAsync(parameters)).ShouldBe(2L);
        count.ExecuteScalar(parameters).ShouldBe(2L);
        pooler.Handovers.ShouldBe(2);
    }

    [Fact]
    public async Task Nothing_of_the_tenant_is_left_in_the_session_for_the_next_user_of_the_connection()
    {
        var tenantContext = new SwitchableTenantContext();
        var options = Options(tenantContext);
        tenantContext.SetTenant(_tenantA.Id);

        await using var db = new SalonDbContext(options, tenantContext);
        await db.Database.OpenConnectionAsync();
        (await db.Services.CountAsync()).ShouldBe(2);
        db.Services.Add(NewService("Perm A"));
        await db.SaveChangesAsync();

        // The same physical connection, asked directly: what a pooler would hand to the next client.
        await using var command = new NpgsqlCommand("SELECT coalesce(current_setting('app.tenant_id', true), '')", (NpgsqlConnection)db.Database.GetDbConnection());
        (await command.ExecuteScalarAsync()).ShouldBe(string.Empty);
    }

    private static DbContextOptions<SalonDbContext> Options(ITenantContext tenantContext, TransactionPoolerSimulator? pooler = null)
        => new DbContextOptionsBuilder<SalonDbContext>()
            .UseNpgsql(AppRole.ConnectionString, npgsql => npgsql.UseNodaTime())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantConnectionInterceptor(tenantContext))
            .AddInterceptors(pooler?.Interceptors ?? [])
            .Options;

    /// <summary>
    /// A second host with the production wiring, empty caches and no request served yet, whose database login is the
    /// non-superuser role; the pooler's interceptors come after the application's own.
    /// </summary>
    private WebApplicationFactory<Program> FreshHostAsAppRole(params IInterceptor[] pooler)
        => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<SalonDbContext>(options => options
                .UseNpgsql(AppRole.ConnectionString, npgsql => npgsql.UseNodaTime())
                .AddInterceptors(pooler))));

    private static async Task<string> FirstPageAsync(WebApplicationFactory<Program> host, string hostName, string path)
    {
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri($"https://{hostName}") });
        using var response = await client.GetAsync(path);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<List<string>> NamesAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().Services.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync();
    }

    private async Task AddServiceAsync(Guid tenantId, string name)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(NewService(name));
        await db.SaveChangesAsync();
    }

    private static Service NewService(string name)
        => new() { Name = name, Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" };

    private sealed class SwitchableTenantContext : IMutableTenantContext
    {
        public Guid? TenantId { get; private set; }

        public void SetTenant(Guid? tenantId) => TenantId = tenantId;
    }
}