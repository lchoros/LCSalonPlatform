using Microsoft.EntityFrameworkCore;

namespace Salon.Data.Tenancy;

/// <summary>Whether two connections the app held open at the same time were served by the same Postgres server process.</summary>
public enum ServerSessionSharing
{
    /// <summary>Each connection kept its own server process: a direct connection, a pooler in session mode, or a pooler that did not show itself this time.</summary>
    NotSeen = 0,

    /// <summary>Certain: a pooler in transaction (or statement) mode sits between the app and Postgres, so session state does not follow a connection.</summary>
    Seen = 1,
}

/// <param name="Sharing">See <see cref="ServerSessionSharing"/>.</param>
/// <param name="LoginBypassesRowLevelSecurity">True for a superuser or a BYPASSRLS role: the second wall of ADR 0002 is then not standing.</param>
public sealed record DatabaseSessionFindings(ServerSessionSharing Sharing, bool LoginBypassesRowLevelSecurity);

/// <summary>
/// Asks Postgres two things nothing else can tell the app (ADR 0090): is there a pooler that hands the statements of
/// one connection to different server processes, and does the login bypass row-level security. Read-only: it holds two
/// connections open and asks each which server process answers. Nothing here is tenant data.
/// </summary>
public static class DatabaseSessionProbe
{
    /// <summary>Twice on the first connection, then once on the second: a pooler that reuses the last free server process shows on the second, one that takes turns shows on the first.</summary>
    private const int Rounds = 3;

    public static async Task<DatabaseSessionFindings> RunAsync(SalonDbContext first, SalonDbContext second, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        await first.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await second.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var onFirst = new List<int>();
                var onSecond = new List<int>();
                for (var round = 0; round < Rounds; round++)
                {
                    onFirst.Add(await ServerProcessAsync(first, cancellationToken).ConfigureAwait(false));
                    onFirst.Add(await ServerProcessAsync(first, cancellationToken).ConfigureAwait(false));
                    onSecond.Add(await ServerProcessAsync(second, cancellationToken).ConfigureAwait(false));
                }

                var bypasses = await first.Database
                    .SqlQueryRaw<bool>("SELECT rolsuper OR rolbypassrls AS \"Value\" FROM pg_roles WHERE rolname = current_user")
                    .SingleAsync(cancellationToken)
                    .ConfigureAwait(false);

                return new DatabaseSessionFindings(Classify(onFirst, onSecond), bypasses);
            }
            finally
            {
                await second.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await first.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Two connections open at once always have a server process each, and keep it, unless a pooler shares them: one
    /// connection answered by two processes, or one process answering both connections, is proof.
    /// </summary>
    public static ServerSessionSharing Classify(IReadOnlyCollection<int> onFirst, IReadOnlyCollection<int> onSecond)
    {
        ArgumentNullException.ThrowIfNull(onFirst);
        ArgumentNullException.ThrowIfNull(onSecond);

        return onFirst.Distinct().Count() > 1 || onSecond.Distinct().Count() > 1 || onFirst.Intersect(onSecond).Any()
            ? ServerSessionSharing.Seen
            : ServerSessionSharing.NotSeen;
    }

    private static Task<int> ServerProcessAsync(SalonDbContext db, CancellationToken cancellationToken)
        => db.Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"").SingleAsync(cancellationToken);
}