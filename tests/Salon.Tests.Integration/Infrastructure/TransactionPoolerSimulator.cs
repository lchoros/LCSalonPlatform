using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Salon.Tests.Integration.Infrastructure;

/// <summary>
/// What a pooler in transaction mode (PgBouncer between the app and Postgres) does to session state, on one physical
/// connection. Such a pooler hands every transaction, and every statement sent on its own, whichever server connection
/// is free, so the session a statement finds is the one some other client left there, not the one the app prepared a
/// moment ago. Only what travels inside the same transaction is certain to arrive.
/// The simulator reproduces exactly that: before each statement that runs outside a transaction, and before each BEGIN,
/// it overwrites the session's <c>app.tenant_id</c> with <paramref name="leftBehind"/>: empty for a fresh server
/// connection, or the salon that used it last. Statements inside a transaction are left alone, as the pooler would.
/// It is a simulation, not PgBouncer: it proves what the code does when session state does not follow it.
/// </summary>
public sealed class TransactionPoolerSimulator(Guid? leftBehind)
{
    /// <summary>Register after the application's own interceptors.</summary>
    public IInterceptor[] Interceptors => [new Statements(this), new Transactions(this)];

    /// <summary>How many times a statement or a transaction was handed "another" server connection.</summary>
    public int Handovers { get; private set; }

    private void HandOver(DbConnection connection)
    {
        using var command = Command(connection);
        command.ExecuteNonQuery();
        Handovers++;
    }

    private async Task HandOverAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = Command(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        Handovers++;
    }

    private DbCommand Command(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @left_behind, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "left_behind";
        parameter.DbType = DbType.String;
        parameter.Value = leftBehind?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);
        return command;
    }

    private sealed class Statements(TransactionPoolerSimulator pooler) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Before(command);
            return result;
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Before(command);
            return result;
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            Before(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await BeforeAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await BeforeAsync(command, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            await BeforeAsync(command, cancellationToken);
            return result;
        }

        private void Before(DbCommand command)
        {
            if (command.Transaction is null)
            {
                pooler.HandOver(command.Connection!);
            }
        }

        private Task BeforeAsync(DbCommand command, CancellationToken cancellationToken)
            => command.Transaction is null ? pooler.HandOverAsync(command.Connection!, cancellationToken) : Task.CompletedTask;
    }

    private sealed class Transactions(TransactionPoolerSimulator pooler) : DbTransactionInterceptor
    {
        public override InterceptionResult<DbTransaction> TransactionStarting(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result)
        {
            pooler.HandOver(connection);
            return result;
        }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        {
            await pooler.HandOverAsync(connection, cancellationToken);
            return result;
        }
    }
}