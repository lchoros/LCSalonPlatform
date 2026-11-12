using Salon.Core.Events;

namespace Salon.Core.Staff.Events;

/// <summary>Raised when a salon adds or removes time off for a staff member (ADR 0015). Ids only; consumers reload the person's availability.</summary>
public sealed record StaffTimeOffChanged(Guid StaffMemberId, Guid TenantId) : IEvent;