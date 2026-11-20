using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Data.Tenancy;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Tenancy;

/// <summary>
/// The probe the heartbeat logs (ADR 0090), against the local database: a direct connection, so no pooler can be seen;
/// the test host's superuser login bypasses row-level security, the role production resembles does not.
/// </summary>
[Collection(SharedDatabase.Name)]
public class DatabaseSessionProbeTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        await AppRole.EnsureAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_direct_connection_shows_no_sharing_and_a_superuser_login_is_named_as_bypassing()
    {
        await using var first = factory.CreateTenantScope(null);
        await using var second = factory.CreateTenantScope(null);

        var findings = await DatabaseSessionProbe.RunAsync(
            first.ServiceProvider.GetRequiredService<SalonDbContext>(),
            second.ServiceProvider.GetRequiredService<SalonDbContext>());

        findings.ShouldBe(new DatabaseSessionFindings(ServerSessionSharing.NotSeen, LoginBypassesRowLevelSecurity: true));
    }

    [Fact]
    public async Task The_non_superuser_role_does_not_bypass_row_level_security()
    {
        var noTenant = new NoTenant();
        var options = new DbContextOptionsBuilder<SalonDbContext>()
            .UseNpgsql(AppRole.ConnectionString, npgsql => npgsql.UseNodaTime())
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(new TenantCommandInterceptor(noTenant))
            .Options;
        await using var first = new SalonDbContext(options, noTenant);
        await using var second = new SalonDbContext(options, noTenant);

        var findings = await DatabaseSessionProbe.RunAsync(first, second);

        findings.ShouldBe(new DatabaseSessionFindings(ServerSessionSharing.NotSeen, LoginBypassesRowLevelSecurity: false));
    }

    [Fact]
    public async Task The_heartbeat_runs_with_the_probe_in_it()
    {
        await factory.CreateTenantAsync("heartbeat-a");
        await using var scope = factory.CreateTenantScope(null);
        var heartbeat = scope.ServiceProvider.GetServices<IScheduleTask>().Single(t => t.SystemName == "System.Heartbeat");

        await Should.NotThrowAsync(() => heartbeat.ExecuteAsync(CancellationToken.None));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid? TenantId => null;
    }
}