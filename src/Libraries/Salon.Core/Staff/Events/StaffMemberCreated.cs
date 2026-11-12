using Salon.Core.Events;

namespace Salon.Core.Staff.Events;

/// <summary>Raised when a salon adds a staff member. Carries ids and the display name only; consumers load what else they need.</summary>
public sealed record StaffMemberCreated(Guid StaffMemberId, Guid TenantId, string DisplayName) : IEvent;