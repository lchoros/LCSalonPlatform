using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Salon.Core.Tenancy;

namespace Salon.Data.Tenancy;

/// <summary>
/// Publishes the current tenant to Postgres as the session setting <c>app.tenant_id</c> every time a connection opens.
/// Row-level security policies (Migrations/RowLevelSecurity) read it, so even raw SQL or a bug that skips the EF filter
/// cannot see or write another tenant's rows. Npgsql resets the setting when a connection returns to the pool.
/// </summary>
public sealed class TenantConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public const string SettingName = "app.tenant_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = CreateCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        var command = CreateCommand(connection);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private DbCommand CreateCommand(DbConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.DbType = DbType.String;
        parameter.Value = tenantContext.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);

        return command;
    }
}