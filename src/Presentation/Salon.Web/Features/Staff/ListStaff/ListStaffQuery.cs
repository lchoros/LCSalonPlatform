using Salon.Core.Cqrs;
using Salon.Core.Staff;

namespace Salon.Web.Features.Staff.ListStaff;

/// <summary>Every staff member of the current salon (active and inactive) with a compressed weekly rota. A query: no transaction, tenant required.</summary>
public sealed record ListStaffQuery : IQuery<IReadOnlyList<StaffListItem>>;

/// <summary>One row of the admin list; <see cref="Hours"/> is already grouped ("Mon–Fri 09:00–17:00") so the view only localizes day names.</summary>
public sealed record StaffListItem(
    Guid Id,
    string DisplayName,
    string Colour,
    bool IsActive,
    bool IsBookable,
    IReadOnlyList<WorkingHoursGroup> Hours);

/// <param name="Staff">In display order; empty shows the empty state.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ListStaffViewModel(IReadOnlyList<StaffListItem> Staff, string? Flash);