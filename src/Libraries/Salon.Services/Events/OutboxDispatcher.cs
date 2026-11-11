using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NodaTime;

using Salon.Core.Events;
using Salon.Data;
using Salon.Services.Tenancy;

namespace Salon.Services.Events;

/// <summary>
/// Loads due rows approximately oldest first (UUIDv7 ids are time-ordered to the millisecond, nothing finer is
/// guaranteed) and runs each event's consumers in a fresh DI scope: the publishing tenant's scope through
/// <see cref="ITenantScopeRunner"/>, or a plain scope for platform events. Every row's outcome is saved on its own, so
/// one poison message never blocks the rest of the batch. Delivery is at-least-once; consumers must be idempotent (ADR 0007).
/// </summary>
internal sealed class OutboxDispatcher(
    SalonDbContext db,
    IClock clock,
    IEventTypeRegistry registry,
    ITenantScopeRunner tenantScopes,
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxDispatcher> logger) : IOutboxDispatcher
{
    private static readonly ConcurrentDictionary<Type, ConsumerInvoker> Invokers = new();

    public async Task<int> DispatchPendingAsync(int? batchSize = null, CancellationToken cancellationToken = default)
    {
        var size = batchSize ?? options.Value.BatchSize;
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1, nameof(batchSize));

        var now = clock.GetCurrentInstant();
        var messages = await db.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.FailedAt == null && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.Id)
            .Take(size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await DispatchOneAsync(message, cancellationToken).ConfigureAwait(false);
            await SaveOutcomeAsync(message, cancellationToken).ConfigureAwait(false);
        }

        return messages.Count;
    }

    private async Task DispatchOneAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            var domainEvent = OutboxMessageFactory.Deserialize(message, registry);
            var invoker = Invokers.GetOrAdd(
                domainEvent.GetType(),
                static type => (ConsumerInvoker)Activator.CreateInstance(typeof(ConsumerInvoker<>).MakeGenericType(type))!);

            var consumerCount = 0;
            await RunInScopeAsync(
                message.TenantId,
                async (services, ct) => consumerCount = await invoker.InvokeAsync(services, domainEvent, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            message.ProcessedAt = clock.GetCurrentInstant();
            if (consumerCount == 0)
            {
                logger.LogDebug("Outbox message {MessageId} ({EventType}) has no consumers; marked processed", message.Id, message.EventType);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            OutboxRetryPolicy.RecordFailure(message, ex, clock.GetCurrentInstant(), options.Value.MaxAttempts);
            if (message.FailedAt is not null)
            {
                logger.LogError(ex, "Outbox message {MessageId} ({EventType}) dead-lettered after {Attempts} attempts", message.Id, message.EventType, message.Attempts);
            }
            else
            {
                logger.LogWarning(ex, "Outbox message {MessageId} ({EventType}) failed on attempt {Attempts}; next attempt at {NextAttemptAt}", message.Id, message.EventType, message.Attempts, message.NextAttemptAt);
            }
        }
    }

    /// <summary>Saves one row's outcome; a save failure is logged and the row detached so the rest of the batch still proceeds.</summary>
    private async Task SaveOutcomeAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // xmin moved: another dispatcher saved this row after we loaded it. Its outcome stands; ours is dropped.
            logger.LogInformation(ex, "Outbox message {MessageId} was updated by another dispatcher; skipping", message.Id);
            db.Entry(message).State = EntityState.Detached;
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(ex, "Could not save the outcome of outbox message {MessageId}; it stays pending for the next pass", message.Id);
            db.Entry(message).State = EntityState.Detached;
        }
    }

    private Task RunInScopeAsync(Guid? tenantId, Func<IServiceProvider, CancellationToken, Task> action, CancellationToken cancellationToken)
        => tenantId is { } tenant
            ? tenantScopes.RunAsync(tenant, action, cancellationToken)
            : RunPlatformAsync(action, cancellationToken);

    private async Task RunPlatformAsync(Func<IServiceProvider, CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await action(scope.ServiceProvider, cancellationToken).ConfigureAwait(false);
    }

    private abstract class ConsumerInvoker
    {
        public abstract Task<int> InvokeAsync(IServiceProvider services, IEvent domainEvent, CancellationToken cancellationToken);
    }

    /// <summary>Resolves the consumers of one event type from the given scope and awaits them in registration order.</summary>
    private sealed class ConsumerInvoker<TEvent> : ConsumerInvoker
        where TEvent : IEvent
    {
        public override async Task<int> InvokeAsync(IServiceProvider services, IEvent domainEvent, CancellationToken cancellationToken)
        {
            var consumers = services.GetServices<IConsumer<TEvent>>().ToList();
            foreach (var consumer in consumers)
            {
                await consumer.HandleAsync((TEvent)domainEvent, cancellationToken).ConfigureAwait(false);
            }

            return consumers.Count;
        }
    }
}