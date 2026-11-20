using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Salon.Core.Tenancy;

namespace Salon.Data.Tenancy;

/// <summary>
/// Sends the current tenant to Postgres with every statement, as the setting <c>app.tenant_id</c> that the row-level
/// security policies read (Migrations/RowLevelSecurity). The setting is always transaction-local
/// (<c>set_config(..., true)</c>) and never left in the session (ADR 0090):
/// <list type="bullet">
/// <item>A query, a bulk change or raw SQL gets the setting as the first statement of its own batch. One batch is one
/// transaction to Postgres and to a pooler, so the two cannot be separated, and the setting is gone when it ends.</item>
/// <item>A save runs in a transaction (<see cref="SalonDbContext"/> always opens one) and the setting is made in that
/// transaction just before the save's statements. The provider reads a save's results by position, so nothing may be
/// put in front of them.</item>
/// </list>
/// The tenant is read at the moment the statement is sent, so one set halfway through a transaction counts from the
/// next statement on. No tenant sends the empty value, which matches no row. A statement that reaches Postgres without
/// passing here (migrations, a command built by hand) finds no setting at all and sees no tenant row either.
/// </summary>
public sealed class TenantCommandInterceptor(ITenantContext tenantContext) : DbCommandInterceptor
{
    public const string SettingName = "app.tenant_id";

    private const string Opening = "SELECT set_config('" + SettingName + "', '";
    private const string Closing = "', true);\n";

    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        if (!result.HasResult)
        {
            Carry(command, eventData);
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (!result.HasResult)
        {
            await CarryAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>The caller's first result set is the second one of the batch: step over the setting's own.</summary>
    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Carries(command))
        {
            result.NextResult();
        }

        return result;
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Carries(command))
        {
            await result.NextResultAsync(cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
    {
        if (!result.HasResult)
        {
            Carry(command, eventData);
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!result.HasResult)
        {
            await CarryAsync(command, eventData, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    /// <summary>
    /// A single value is the first column of the first row of the first result set, which would be the setting's. So
    /// the batch is read here and the value of the caller's own statement is handed back.
    /// </summary>
    public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
    {
        if (result.HasResult || !Carry(command, eventData))
        {
            return result;
        }

        using var reader = command.ExecuteReader();
        var value = reader.NextResult() && reader.Read() ? reader.GetValue(0) : null;
        return InterceptionResult<object>.SuppressWithResult(value!);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        if (result.HasResult || !await CarryAsync(command, eventData, cancellationToken).ConfigureAwait(false))
        {
            return result;
        }

        var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            var value = await reader.NextResultAsync(cancellationToken).ConfigureAwait(false) && await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? reader.GetValue(0)
                : null;
            return InterceptionResult<object>.SuppressWithResult(value!);
        }
    }

    /// <returns>True when the setting now leads the command's own batch.</returns>
    private bool Carry(DbCommand command, CommandEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(eventData);

        switch (eventData.CommandSource)
        {
            case CommandSource.Migrations:
                return false;
            case CommandSource.SaveChanges:
                using (var setting = SettingInTransactionOf(command))
                {
                    setting.ExecuteNonQuery();
                }

                return false;
            default:
                Lead(command);
                return true;
        }
    }

    private async ValueTask<bool> CarryAsync(DbCommand command, CommandEventData eventData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(eventData);

        switch (eventData.CommandSource)
        {
            case CommandSource.Migrations:
                return false;
            case CommandSource.SaveChanges:
                var setting = SettingInTransactionOf(command);
                await using (setting.ConfigureAwait(false))
                {
                    await setting.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                return false;
            default:
                Lead(command);
                return true;
        }
    }

    private static bool Carries(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.CommandText.StartsWith(Opening, StringComparison.Ordinal);
    }

    /// <summary>
    /// The value is a Guid this class formats itself, or nothing: hexadecimal digits and hyphens only, so it is written
    /// into the statement instead of joining the caller's parameters.
    /// </summary>
    private string Setting() => Opening + tenantContext.TenantId?.ToString("D") + Closing;

#pragma warning disable CA2100 // The text put in front is Setting() above (a Guid or nothing); the rest is the caller's own statement, untouched.
    private void Lead(DbCommand command)
    {
        var own = command.CommandText;
        if (Carries(command))
        {
            // The same command sent again: the tenant may have changed since, so the old setting goes.
            own = own[(own.IndexOf(Closing, StringComparison.Ordinal) + Closing.Length)..];
        }

        command.CommandText = Setting() + own;
    }

    private DbCommand SettingInTransactionOf(DbCommand command)
    {
        if (command.Transaction is null || command.Connection is null)
        {
            // Outside a transaction the setting would end before the save's statements began. Refuse instead of
            // writing rows under no tenant; SalonDbContext sets AutoTransactionBehavior.Always so this cannot happen.
            throw new InvalidOperationException(
                $"A save must run in a transaction so the tenant setting reaches it; keep {nameof(AutoTransactionBehavior)}.{nameof(AutoTransactionBehavior.Always)} on the context (ADR 0090).");
        }

        var setting = command.Connection.CreateCommand();
        setting.Transaction = command.Transaction;
        setting.CommandText = Setting();
        return setting;
    }
#pragma warning restore CA2100
}