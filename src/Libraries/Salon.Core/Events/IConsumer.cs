namespace Salon.Core.Events;

/// <summary>Handles one event type. Register as many consumers per event as needed.</summary>
public interface IConsumer<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken = default);
}