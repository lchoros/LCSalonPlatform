using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Catalog;
using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Tasks;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Events;
using Salon.Tests.Integration.Infrastructure;

namespace Salon.Tests.Integration.Events;

/// <summary>
/// ADR 0007: publishing writes an outbox row inside the caller's transaction; the dispatcher runs consumers in the
/// publishing tenant's scope, retries with backoff and dead-letters after the attempt limit.
/// </summary>
[Collection(SharedDatabase.Name)]
public class OutboxTests(SalonWebApplicationFactory factory) : IAsyncLifetime
{
    private Tenant _tenant = null!;
    private Tenant _otherTenant = null!;

    public async Task InitializeAsync()
    {
        await factory.ResetDatabaseAsync();
        OutboxProbeConsumer.Calls.Clear();
        _tenant = await factory.CreateTenantAsync("outbox-a");
        _otherTenant = await factory.CreateTenantAsync("outbox-b", "pl-PL", "PLN");
        // One service each, so a consumer running under the right tenant sees exactly one.
        await AddServiceAsync(_tenant.Id, "Cut A");
        await AddServiceAsync(_otherTenant.Id, "Strzyżenie B");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publishing_in_a_tenant_scope_writes_one_row_with_that_tenant_and_the_json_payload()
    {
        var probeId = Guid.NewGuid();
        var before = SystemClock.Instance.GetCurrentInstant();

        await PublishAsync(_tenant.Id, new OutboxProbeEvent(probeId, Fail: false));

        var row = await SingleRowAsync();
        row.TenantId.ShouldBe(_tenant.Id);
        row.EventType.ShouldBe("Salon.Tests.Integration.Events.OutboxProbeEvent");
        using var payload = JsonDocument.Parse(row.Payload);
        payload.RootElement.GetProperty("probeId").GetGuid().ShouldBe(probeId);
        payload.RootElement.GetProperty("fail").GetBoolean().ShouldBeFalse();
        row.OccurredAt.ShouldBeGreaterThanOrEqualTo(before);
        row.ProcessedAt.ShouldBeNull();
        row.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task A_command_handler_that_publishes_and_then_throws_leaves_no_row()
    {
        await using var scope = factory.CreateTenantScope(_tenant.Id);
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<InvalidOperationException>(
            () => dispatcher.SendAsync(new PublishProbe(Guid.NewGuid(), ThenFail: true), CancellationToken.None));

        (await RowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_command_handler_that_publishes_and_succeeds_commits_the_row_with_the_command()
    {
        var probeId = Guid.NewGuid();
        await using var scope = factory.CreateTenantScope(_tenant.Id);

        var result = await scope.ServiceProvider.GetRequiredService<IDispatcher>().SendAsync(new PublishProbe(probeId, ThenFail: false), CancellationToken.None);

        result.ShouldBe(probeId);
        (await SingleRowAsync()).TenantId.ShouldBe(_tenant.Id);
    }

    [Fact]
    public async Task Dispatch_runs_the_consumer_inside_the_publishing_tenant_scope_and_marks_the_row_processed()
    {
        var probeId = Guid.NewGuid();
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(probeId, Fail: false));

        var processed = await DispatchAsync();

        processed.ShouldBe(1);
        // Exactly one visible service: the tenant filter (and RLS session setting) applied inside the consumer's scope.
        CallsFor(probeId).ShouldBe([new ProbeCall(probeId, _tenant.Id, VisibleServices: 1)]);
        var row = await SingleRowAsync();
        row.ProcessedAt.ShouldNotBeNull();
        row.Attempts.ShouldBe(0);
        row.LastError.ShouldBeNull();
    }

    [Fact]
    public async Task Processed_rows_are_never_dispatched_twice()
    {
        var probeId = Guid.NewGuid();
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(probeId, Fail: false));
        await DispatchAsync();

        var second = await DispatchAsync();

        second.ShouldBe(0);
        CallsFor(probeId).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_platform_event_dispatches_with_no_tenant_in_scope()
    {
        var probeId = Guid.NewGuid();
        await PublishAsync(null, new OutboxProbeEvent(probeId, Fail: false));

        await DispatchAsync();

        (await SingleRowAsync()).TenantId.ShouldBeNull();
        CallsFor(probeId).ShouldBe([new ProbeCall(probeId, null, VisibleServices: 0)]);
    }

    [Fact]
    public async Task A_failing_consumer_schedules_a_retry_and_dead_letters_after_the_attempt_limit()
    {
        var probeId = Guid.NewGuid();
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(probeId, Fail: true));
        var maxAttempts = factory.Services.GetRequiredService<IOptions<OutboxOptions>>().Value.MaxAttempts;

        (await DispatchAsync()).ShouldBe(1);

        var row = await SingleRowAsync();
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldNotBeNull().ShouldContain("InvalidOperationException: probe");
        row.LastError.ShouldContain("failed on purpose");
        row.NextAttemptAt.ShouldNotBeNull();
        row.NextAttemptAt!.Value.ShouldBeGreaterThan(SystemClock.Instance.GetCurrentInstant());
        row.ProcessedAt.ShouldBeNull();
        row.FailedAt.ShouldBeNull();

        // Not due yet: nothing happens.
        (await DispatchAsync()).ShouldBe(0);
        CallsFor(probeId).Count.ShouldBe(1);

        for (var attempt = 2; attempt <= maxAttempts; attempt++)
        {
            await MakeDueAsync(row.Id);
            (await DispatchAsync()).ShouldBe(1);
        }

        row = await SingleRowAsync();
        row.Attempts.ShouldBe(maxAttempts);
        row.FailedAt.ShouldNotBeNull();
        row.ProcessedAt.ShouldBeNull();
        CallsFor(probeId).Count.ShouldBe(maxAttempts);

        // Dead-lettered rows are not retried even when due.
        await MakeDueAsync(row.Id);
        (await DispatchAsync()).ShouldBe(0);
        CallsFor(probeId).Count.ShouldBe(maxAttempts);
    }

    [Fact]
    public async Task Every_consumer_gets_its_turn_and_one_that_handled_the_event_is_not_run_again_on_the_retry()
    {
        var probeId = Guid.NewGuid();
        await PublishAsync(_tenant.Id, new OutboxFlakyEvent(probeId));

        // First attempt: the flaky consumer throws, the steady one still runs, whichever comes first.
        (await DispatchAsync()).ShouldBe(1);
        var row = await SingleRowAsync();
        row.Attempts.ShouldBe(1);
        row.ProcessedAt.ShouldBeNull();
        row.LastError.ShouldNotBeNull().ShouldContain("the flaky consumer failed its first call on purpose");
        row.CompletedConsumers.ShouldBe([typeof(OutboxSteadyConsumer).FullName!]);
        OutboxSteadyConsumer.Calls[probeId].ShouldBe(1);
        OutboxFlakyConsumer.Calls[probeId].ShouldBe(1);

        // The retry is for the one that failed: the steady consumer is left alone.
        await MakeDueAsync(row.Id);
        (await DispatchAsync()).ShouldBe(1);
        row = await SingleRowAsync();
        row.ProcessedAt.ShouldNotBeNull();
        row.CompletedConsumers.ShouldBe([typeof(OutboxSteadyConsumer).FullName!, typeof(OutboxFlakyConsumer).FullName!], ignoreOrder: true);
        OutboxFlakyConsumer.Calls[probeId].ShouldBe(2);
        OutboxSteadyConsumer.Calls[probeId].ShouldBe(1, "what was delivered is not delivered again");
    }

    [Fact]
    public async Task One_poison_message_does_not_block_the_rest_of_the_batch()
    {
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(bad, Fail: true));
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(good, Fail: false));

        (await DispatchAsync()).ShouldBe(2);

        var rows = await RowsAsync();
        rows.Count(r => r.ProcessedAt != null).ShouldBe(1);
        rows.Count(r => r.Attempts == 1 && r.ProcessedAt == null).ShouldBe(1);
        CallsFor(good).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failure_message_with_a_nul_character_is_still_recorded_and_does_not_block_the_next_row()
    {
        var bad = Guid.NewGuid();
        var good = Guid.NewGuid();
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(bad, Fail: true, FailureText: "boom{NUL}bang"));
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(good, Fail: false));

        (await DispatchAsync()).ShouldBe(2);

        var rows = await RowsAsync();
        var failed = rows.Single(r => r.ProcessedAt == null);
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldNotBeNull().ShouldContain("boombang");
        failed.LastError.ShouldNotContain("\0");
        rows.Count(r => r.ProcessedAt != null).ShouldBe(1);
        CallsFor(good).Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_event_with_no_consumer_is_marked_processed()
    {
        await PublishAsync(_tenant.Id, new OutboxOrphanEvent(Guid.NewGuid()));

        (await DispatchAsync()).ShouldBe(1);

        (await SingleRowAsync()).ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Batches_are_limited_to_the_requested_size()
    {
        var probes = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(probes[0], Fail: false));
        await PublishAsync(null, new OutboxProbeEvent(probes[1], Fail: false));
        await PublishAsync(_tenant.Id, new OutboxProbeEvent(probes[2], Fail: false));

        (await DispatchAsync(batchSize: 2)).ShouldBe(2);

        // No ordering guarantee (ADR 0007): exactly two of the three were handled, each once.
        probes.Count(p => CallsFor(p).Count == 1).ShouldBe(2);
        probes.Count(p => CallsFor(p).Count == 0).ShouldBe(1);

        (await DispatchAsync(batchSize: 2)).ShouldBe(1);

        probes.ShouldAllBe(p => CallsFor(p).Count == 1);
    }

    [Fact]
    public async Task The_schedule_task_drains_the_outbox()
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
        {
            await PublishAsync(_tenant.Id, new OutboxProbeEvent(id, Fail: false));
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var task = scope.ServiceProvider.GetServices<IScheduleTask>().Single(t => t.SystemName == "Events.DispatchOutbox");
        await task.ExecuteAsync(CancellationToken.None);

        (await RowsAsync()).ShouldAllBe(r => r.ProcessedAt != null);
        ids.ShouldAllBe(id => CallsFor(id).Count == 1);
    }

    private static List<ProbeCall> CallsFor(Guid probeId)
        => OutboxProbeConsumer.Calls.Where(c => c.ProbeId == probeId).ToList();

    private async Task AddServiceAsync(Guid tenantId, string name)
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        db.Services.Add(new Service { Name = name, Duration = Duration.FromMinutes(30), Price = 20m, Currency = "GBP" });
        await db.SaveChangesAsync();
    }

    private async Task PublishAsync<TEvent>(Guid? tenantId, TEvent domainEvent)
        where TEvent : IEvent
    {
        await using var scope = factory.CreateTenantScope(tenantId);
        await scope.ServiceProvider.GetRequiredService<IEventPublisher>().PublishAsync(domainEvent);
    }

    private async Task<int> DispatchAsync(int? batchSize = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(batchSize);
    }

    private async Task MakeDueAsync(Guid messageId)
    {
        await using var scope = factory.CreateTenantScope(null);
        var db = scope.ServiceProvider.GetRequiredService<SalonDbContext>();
        var row = await db.OutboxMessages.SingleAsync(m => m.Id == messageId);
        row.NextAttemptAt = SystemClock.Instance.GetCurrentInstant() - Duration.FromMinutes(1);
        await db.SaveChangesAsync();
    }

    private async Task<List<OutboxMessage>> RowsAsync()
    {
        await using var scope = factory.CreateTenantScope(null);
        return await scope.ServiceProvider.GetRequiredService<SalonDbContext>().OutboxMessages.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
    }

    private async Task<OutboxMessage> SingleRowAsync() => (await RowsAsync()).ShouldHaveSingleItem();
}