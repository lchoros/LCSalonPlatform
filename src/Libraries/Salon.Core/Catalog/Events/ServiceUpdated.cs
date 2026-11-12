using Salon.Core.Events;

namespace Salon.Core.Catalog.Events;

/// <summary>Raised when a salon edits a service (any field, including reactivation). Ids only; consumers load what they need.</summary>
public sealed record ServiceUpdated(Guid ServiceId, Guid TenantId) : IEvent;