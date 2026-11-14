using System.Collections.Concurrent;
using Salon.Core.Events;

namespace Salon.Tests.Integration.Events;

/// <summary>Test event with two consumers: one that fails the first time it sees it and one that never fails.</summary>
public sealed record OutboxFlakyEvent(Guid ProbeId) : IEvent;

/// <summary>Counts calls per probe and throws on the first one, like a sender whose server was down for a moment.</summary>
public sealed class OutboxFlakyConsumer : IConsumer<OutboxFlakyEvent>
{
    public static ConcurrentDictionary<Guid, int> Calls { get; } = new();

    public Task HandleAsync(OutboxFlakyEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var call = Calls.AddOrUpdate(domainEvent.ProbeId, 1, (_, seen) => seen + 1);
        return call == 1 ? throw new InvalidOperationException("the flaky consumer failed its first call on purpose") : Task.CompletedTask;
    }
}

/// <summary>Counts calls per probe and always succeeds, like a sender whose message must not go out twice.</summary>
public sealed class OutboxSteadyConsumer : IConsumer<OutboxFlakyEvent>
{
    public static ConcurrentDictionary<Guid, int> Calls { get; } = new();

    public Task HandleAsync(OutboxFlakyEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        Calls.AddOrUpdate(domainEvent.ProbeId, 1, (_, seen) => seen + 1);
        return Task.CompletedTask;
    }
}