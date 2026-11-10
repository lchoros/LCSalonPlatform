namespace Salon.Core.Events;

/// <summary>Marker for domain events. Events cross module boundaries; slices never call each other directly.</summary>
public interface IEvent;