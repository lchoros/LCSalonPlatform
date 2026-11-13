using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using Salon.Core.Booking;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Tests.Integration.Clients;
using Salon.Tests.Integration.Infrastructure;
using Salon.Web.Framework.Booking;

namespace Salon.Tests.Integration.Tasks;

/// <summary>
/// The retention task of ADR 0017 against the real tables: rows older than the retention go, in every tenant, whatever
/// their state; fresh rows stay, including verified ones that a repeated POST may still need.
/// </summary>
[Collection(SharedDatabase.Name)]
public class PhoneVerificationCleanupTaskTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _a = null!;
    private Tenant _b = null!;
    private Guid _serviceA;
    private Guid _serviceB;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        _a = await factory.CreateTenantAsync("retention-a");
        _b = await factory.CreateTenantAsync("retention-b", "pl-PL", "PLN");
        (_, _serviceA) = await VisitSeed.SalonAsync(factory, _a.Id);
        (_, _serviceB) = await VisitSeed.SalonAsync(factory, _b.Id);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void The_task_is_registered_hourly_under_its_system_name_with_a_day_of_retention_by_default()
    {
        using var scope = factory.Services.CreateScope();
        var task = scope.ServiceProvider.GetServices<IScheduleTask>().Single(t => t.SystemName == "Booking.CleanupVerifications");

        task.CronExpression.ShouldBe("0 * * * *");
        scope.ServiceProvider.GetRequiredService<IOptions<PublicBookingOptions>>().Value.VerificationRetention.ShouldBe(TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task Rows_older_than_the_retention_are_deleted_in_every_tenant_and_fresh_ones_stay()
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var oldA = await RowAsync(_a.Id, _serviceA, now - Duration.FromHours(25), verified: true);
        var oldDeadA = await RowAsync(_a.Id, _serviceA, now - Duration.FromDays(3), attempts: PhoneVerificationRules.MaxAttempts);
        var recentA = await RowAsync(_a.Id, _serviceA, now - Duration.FromHours(23), verified: true);
        var freshA = await RowAsync(_a.Id, _serviceA, now - Duration.FromMinutes(2));
        var oldB = await RowAsync(_b.Id, _serviceB, now - Duration.FromHours(30));
        var freshB = await RowAsync(_b.Id, _serviceB, now - Duration.FromMinutes(1));

        using (var scope = factory.Services.CreateScope())
        {
            var task = scope.ServiceProvider.GetServices<IScheduleTask>().Single(t => t.SystemName == "Booking.CleanupVerifications");
            await task.ExecuteAsync(CancellationToken.None);
        }

        (await IdsAsync(_a.Id)).ShouldBe([recentA, freshA], ignoreOrder: true);
        (await IdsAsync(_b.Id)).ShouldBe([freshB]);
        _ = (oldA, oldDeadA, oldB);
    }

    private async Task<Guid> RowAsync(Guid tenantId, Guid serviceId, Instant createdAt, bool verified = false, int attempts = 0)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var salt = PhoneVerificationRules.GenerateSalt();
        var row = new PhoneVerification
        {
            TenantId = tenantId,
            PhoneE164 = "+447700900123",
            CodeSalt = salt,
            CodeHash = PhoneVerificationRules.Hash(salt, "123456"),
            CreatedAt = createdAt,
            ExpiresAt = createdAt + PhoneVerificationRules.Lifetime,
            Attempts = attempts,
            VerifiedAt = verified ? createdAt + Duration.FromMinutes(1) : null,
            ServiceId = serviceId,
            Start = createdAt + Duration.FromDays(1),
            FirstName = "Olivia",
            LastName = "Brown",
        };
        db.PhoneVerifications.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<List<Guid>> IdsAsync(Guid tenantId)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().PhoneVerifications.AsNoTracking().Select(v => v.Id).ToListAsync();
    }
}