using Salon.Core.Cqrs;

namespace Salon.Web.Features.Catalog.StaffOverrides;

/// <summary>
/// Replaces the staff overrides of one service in one go (ADR 0018) and returns false when the salon has no such service.
/// <see cref="SaveStaffOverridesValidator"/> holds the rules; the handler upserts a row per person who differs from the
/// defaults, deletes the rows of people set back to them, ignores people the salon cannot book, and publishes
/// <c>ServiceUpdated</c>, all inside the pipeline's transaction.
/// </summary>
/// <param name="Rows">One per bookable staff member as the form posted them; a missing person is left as they were.</param>
public sealed record SaveStaffOverridesCommand(Guid ServiceId, IReadOnlyList<StaffOverrideInput> Rows) : ICommand<bool>;

/// <param name="Offers">False when this person never does this service.</param>
/// <param name="DurationMinutes">Blank (null) means the service duration.</param>
/// <param name="Price">Blank (null) means the service price.</param>
public sealed record StaffOverrideInput(Guid StaffMemberId, bool Offers, int? DurationMinutes, decimal? Price);