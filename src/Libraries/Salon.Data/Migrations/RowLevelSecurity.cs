using Microsoft.EntityFrameworkCore.Migrations;

namespace Salon.Data.Migrations;

/// <summary>
/// Every tenant-owned table gets a forced row-level-security policy keyed on the session setting <c>app.tenant_id</c>.
/// Call <see cref="Enable"/> from the migration that creates the table; the integration test
/// RowLevelSecurityCoverageTests fails if a tenant table is missing it.
/// </summary>
internal static class RowLevelSecurity
{
    public const string PolicyName = "tenant_isolation";

    private const string TenantMatch = "tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid";

    public static void Enable(MigrationBuilder migrationBuilder, string table, string schema = "salon")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"""
            ALTER TABLE {schema}.{table} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {schema}.{table} FORCE ROW LEVEL SECURITY;
            CREATE POLICY {PolicyName} ON {schema}.{table}
                USING ({TenantMatch})
                WITH CHECK ({TenantMatch});
            """);
    }

    public static void Disable(MigrationBuilder migrationBuilder, string table, string schema = "salon")
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.Sql($"""
            DROP POLICY IF EXISTS {PolicyName} ON {schema}.{table};
            ALTER TABLE {schema}.{table} NO FORCE ROW LEVEL SECURITY;
            ALTER TABLE {schema}.{table} DISABLE ROW LEVEL SECURITY;
            """);
    }
}
