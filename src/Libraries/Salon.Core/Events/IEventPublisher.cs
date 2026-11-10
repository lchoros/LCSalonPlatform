namespace Salon.Core.Events;

/// <summary>Publishes a domain event. The implementation writes to the transactional outbox, not straight to consumers.</summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}