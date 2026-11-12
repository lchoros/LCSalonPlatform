using Salon.Core.Events;

namespace Salon.Core.Clients.Events;

/// <summary>
/// Raised when a duplicate card (<paramref name="SourceId"/>, now inactive) was merged into <paramref name="TargetId"/>
/// (ADR 0017). Ids only; consumers that cache a client id re-point it to the target.
/// </summary>
public sealed record ClientsMerged(Guid TargetId, Guid SourceId, Guid TenantId) : IEvent;