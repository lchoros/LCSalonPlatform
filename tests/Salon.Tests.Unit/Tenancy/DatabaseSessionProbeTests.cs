using Salon.Data.Tenancy;

namespace Salon.Tests.Unit.Tenancy;

/// <summary>
/// ADR 0090: two connections open at once each keep one Postgres server process, unless a pooler in transaction mode
/// shares them. The numbers are server process ids as each connection saw them, in order.
/// </summary>
public class DatabaseSessionProbeTests
{
    [Fact]
    public void Each_connection_keeping_its_own_server_process_shows_no_sharing()
    {
        DatabaseSessionProbe.Classify([101, 101, 101, 101], [202, 202]).ShouldBe(ServerSessionSharing.NotSeen);
    }

    [Fact]
    public void One_server_process_answering_both_connections_is_sharing()
    {
        DatabaseSessionProbe.Classify([101, 101, 101, 101], [101, 101]).ShouldBe(ServerSessionSharing.Seen);
    }

    [Fact]
    public void One_connection_answered_by_two_server_processes_is_sharing()
    {
        DatabaseSessionProbe.Classify([101, 202, 101, 202], [303, 303]).ShouldBe(ServerSessionSharing.Seen);
        DatabaseSessionProbe.Classify([101, 101], [202, 303]).ShouldBe(ServerSessionSharing.Seen);
    }
}