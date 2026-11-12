using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using NodaTime;

using Npgsql;

using Salon.Core.Catalog;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tenancy;

/// <summary>
/// Postgres enforces tenant isolation on its own: a non-superuser role (the one production will use) sees only rows
/// matching the session setting app.tenant_id, even with raw SQL. Also checks every tenant table has the policy.
/// </summary>
[Collection(SharedDatabase.Name)]
public class RowLevelSecurityTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _tenantA = null!;
    private Tenant _tenantB = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _tenantA = await factory.CreateTenantAsync("rls-a");
        _tenantB = await factory.CreateTenantAsync("rls-b");

        await using var scope = factory.CreateTenantScope(_tenantA.Id);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(new Service { Name = "Only A", Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" });
        db.ServiceCategories.Add(new ServiceCategory { Name = "Only A's category" });
        await db.SaveChangesAsync();

        await AppRole.EnsureAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task App_role_sees_only_the_service_categories_of_the_tenant_in_the_session_setting()
    {
        (await CountAsAppRoleAsync(_tenantA.Id, "salon.service_categories")).ShouldBe(1);
        (await CountAsAppRoleAsync(_tenantB.Id, "salon.service_categories")).ShouldBe(0);
        (await CountAsAppRoleAsync(null, "salon.service_categories")).ShouldBe(0);
    }

    [Fact]
    public async Task Every_tenant_table_has_forced_row_level_security_and_a_policy()
    {
        await using var scope = factory.CreateTenantScope(null);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var tenantTables = db.Model.GetEntityTypes()
            .Where(e => typeof(ITenantEntity).IsAssignableFrom(e.ClrType))
            .Select(e => e.GetTableName()!)
            .ToList();
        tenantTables.ShouldNotBeEmpty();

        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();
        foreach (var table in tenantTables)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT c.relrowsecurity, c.relforcerowsecurity,
                       (SELECT count(*) FROM pg_policy p WHERE p.polrelid = c.oid)
                FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'salon' AND c.relname = @table
                """, connection);
            command.Parameters.AddWithValue("table", table);
            await using var reader = await command.ExecuteReaderAsync();
            (await reader.ReadAsync()).ShouldBeTrue($"table {table} not found");
            reader.GetBoolean(0).ShouldBeTrue($"{table}: row level security not enabled");
            reader.GetBoolean(1).ShouldBeTrue($"{table}: row level security not forced");
            reader.GetInt64(2).ShouldBeGreaterThan(0, $"{table}: no policy");
        }
    }

    [Fact]
    public async Task App_role_sees_only_rows_of_the_tenant_in_the_session_setting()
    {
        (await CountAsAppRoleAsync(_tenantA.Id)).ShouldBe(1);
        (await CountAsAppRoleAsync(_tenantB.Id)).ShouldBe(0);
        (await CountAsAppRoleAsync(null)).ShouldBe(0);
    }

    [Fact]
    public async Task App_role_cannot_insert_a_row_for_another_tenant()
    {
        await using var connection = await OpenAsAppRoleAsync(_tenantA.Id);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO salon.services (id, tenant_id, name, duration, price, currency, is_active, sort_order)
            VALUES (@id, @tenant, 'Smuggled', interval '30 minutes', 10, 'GBP', true, 0)
            """, connection);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("tenant", _tenantB.Id);

        var ex = await Should.ThrowAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    private static async Task<NpgsqlConnection> OpenAsAppRoleAsync(Guid? tenantId)
    {
        var connection = new NpgsqlConnection(AppRole.ConnectionString);
        await connection.OpenAsync();
        await using var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tenant, false)", connection);
        set.Parameters.AddWithValue("tenant", tenantId?.ToString() ?? string.Empty);
        await set.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task<long> CountAsAppRoleAsync(Guid? tenantId, string table = "salon.services")
    {
        await using var connection = await OpenAsAppRoleAsync(tenantId);
#pragma warning disable CA2100 // The table name is one of two literals in this file, not user input.
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
#pragma warning restore CA2100
        return (long)(await command.ExecuteScalarAsync())!;
    }
}