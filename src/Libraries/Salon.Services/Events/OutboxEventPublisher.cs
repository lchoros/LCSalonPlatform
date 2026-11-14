using NodaTime;
using Salon.Core.Events;
using Salon.Core.Tenancy;
using Salon.Data;

namespace Salon.Services.Events;

/// <summary>
/// Writes the event to <c>outbox_messages</c> through the scoped <see cref="SalonDbContext"/>. Inside a command the
/// pipeline's transaction (ADR 0003) already covers the context, so the row commits or rolls back together with the
/// handler's own writes; outside a command SaveChanges commits it at once. Consumers are never called here (ADR 0007).
/// </summary>
internal sealed class OutboxEventPublisher(SalonDbContext db, ITenantContext tenantContext, IClock clock, IEventTypeRegistry registry) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        db.OutboxMessages.Add(OutboxMessageFactory.Create(domainEvent, tenantContext.TenantId, clock.GetCurrentInstant(), registry));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}