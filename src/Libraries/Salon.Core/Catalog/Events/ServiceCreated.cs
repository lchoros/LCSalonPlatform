using Salon.Core.Events;

namespace Salon.Core.Catalog.Events;

/// <summary>Raised when a salon adds a service to its catalogue. Carries ids and the name only; consumers load what else they need.</summary>
public sealed record ServiceCreated(Guid ServiceId, Guid TenantId, string Name) : IEvent;