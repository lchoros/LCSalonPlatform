using Npgsql;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// A non-superuser login that production will resemble. Superusers bypass row-level security, so only queries made as
/// this role prove the policies. Created on demand with the superuser test connection.
/// </summary>
public static class AppRole
{
    public const string Name = "salon_app_test";

    public static string ConnectionString => new NpgsqlConnectionStringBuilder(SalonWebApplicationFactory.TestConnectionString)
    {
        Username = Name,
        Password = Name,
    }.ConnectionString;

    public static async Task EnsureAsync()
    {
        await using var connection = new NpgsqlConnection(SalonWebApplicationFactory.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"""
            DO $$ BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{Name}') THEN
                    CREATE ROLE {Name} LOGIN PASSWORD '{Name}';
                END IF;
            END $$;
            GRANT USAGE ON SCHEMA salon TO {Name};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA salon TO {Name};
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}