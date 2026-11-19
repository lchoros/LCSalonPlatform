namespace Salon.Core.Events;

/// <summary>Publishes a domain event. The implementation writes to the transactional outbox, not straight to consumers.</summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IEvent;

    /// <summary>
    /// Adds the event to the outbox without saving: the caller's own SaveChanges writes it together with the rows it
    /// is about, so a batch of many records and their events is one write that commits or rolls back whole (ADR 0067).
    /// </summary>
    void Queue<TEvent>(TEvent domainEvent)
        where TEvent : IEvent;
}