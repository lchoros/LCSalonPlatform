using System.Text.Json;
using NodaTime;
using Salon.Core.Events;

namespace Salon.Services.Events;

/// <summary>The pure half of publishing and dispatching: an event ↔ an <see cref="OutboxMessage"/> row.</summary>
internal static class OutboxMessageFactory
{
    public static OutboxMessage Create(IEvent domainEvent, Guid? tenantId, Instant now, IEventTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(registry);

        // Serialise as the runtime type: serialising as IEvent would write an empty object.
        var eventType = domainEvent.GetType();
        return new OutboxMessage
        {
            TenantId = tenantId,
            EventType = registry.NameOf(eventType),
            Payload = JsonSerializer.Serialize(domainEvent, eventType, EventJson.Options),
            OccurredAt = now,
        };
    }

    public static IEvent Deserialize(OutboxMessage message, IEventTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(registry);

        var eventType = registry.Resolve(message.EventType);
        return JsonSerializer.Deserialize(message.Payload, eventType, EventJson.Options) as IEvent
            ?? throw new InvalidOperationException($"Outbox message {message.Id} has no usable payload for '{message.EventType}'.");
    }
}