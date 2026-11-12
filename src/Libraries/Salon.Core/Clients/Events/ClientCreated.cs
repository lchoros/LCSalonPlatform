using Salon.Core.Events;

namespace Salon.Core.Clients.Events;

/// <summary>Raised when a salon adds a client. Ids only: a client's name, contact details and health data never travel through the outbox (ADR 0011).</summary>
public sealed record ClientCreated(Guid ClientId, Guid TenantId) : IEvent;