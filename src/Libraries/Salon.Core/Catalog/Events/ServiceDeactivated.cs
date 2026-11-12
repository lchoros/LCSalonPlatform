using Salon.Core.Events;

namespace Salon.Core.Catalog.Events;

/// <summary>Raised when an edit takes a service off the public list (active → inactive). Ids only; consumers load what they need.</summary>
public sealed record ServiceDeactivated(Guid ServiceId, Guid TenantId) : IEvent;