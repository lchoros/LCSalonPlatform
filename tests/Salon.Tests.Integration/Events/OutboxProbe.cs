using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Tests.Integration.Events;

/// <summary>Test event; <see cref="Fail"/> makes its consumer throw, with <see cref="FailureText"/> as the message when given.</summary>
public sealed record OutboxProbeEvent(Guid ProbeId, bool Fail, string? FailureText = null) : IEvent;

/// <summary>Test event that nobody consumes.</summary>
public sealed record OutboxOrphanEvent(Guid ProbeId) : IEvent;

/// <summary>What the consumer saw: the tenant in scope and how many services the tenant-filtered DbContext returned.</summary>
public sealed record ProbeCall(Guid ProbeId, Guid? TenantId, int VisibleServices);

/// <summary>Records each handled probe; registered by the test host's AddSalonEvents scan.</summary>
public sealed class OutboxProbeConsumer(ITenantContext tenantContext, SalonDbContext db) : IConsumer<OutboxProbeEvent>
{
    public static ConcurrentBag<ProbeCall> Calls { get; } = new();

    public async Task HandleAsync(OutboxProbeEvent domainEvent, CancellationToken cancellationToken = default)
    {
        // Counting through the tenant filter proves the data path runs under the publishing tenant, not only the context value.
        var visibleServices = await db.Services.CountAsync(cancellationToken);
        Calls.Add(new ProbeCall(domainEvent.ProbeId, tenantContext.TenantId, visibleServices));

        if (domainEvent.Fail)
        {
            // "{NUL}" becomes a real NUL here: jsonb would refuse it inside the payload, which is exactly why the error text must be sanitised.
            throw new InvalidOperationException(domainEvent.FailureText?.Replace("{NUL}", "\0", StringComparison.Ordinal) ?? $"probe {domainEvent.ProbeId} failed on purpose");
        }
    }
}

/// <summary>A command whose handler publishes and then, optionally, throws: proves the outbox row shares the command's transaction.</summary>
public sealed record PublishProbe(Guid ProbeId, bool ThenFail) : ICommand<Guid>;

public sealed class PublishProbeHandler(IEventPublisher publisher) : IRequestHandler<PublishProbe, Guid>
{
    public async Task<Guid> HandleAsync(PublishProbe request, CancellationToken cancellationToken)
    {
        await publisher.PublishAsync(new OutboxProbeEvent(request.ProbeId, Fail: false), cancellationToken);
        return request.ThenFail ? throw new InvalidOperationException("handler failed after publishing") : request.ProbeId;
    }
}