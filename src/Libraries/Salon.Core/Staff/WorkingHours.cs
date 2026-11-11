using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Staff;

/// <summary>
/// One range of a staff member's weekly rota (glossary: "Grafik"), ADR 0010: on <see cref="Day"/> the person works from
/// <see cref="Start"/> to <see cref="End"/>. Times are local to the salon (the tenant's <c>TimeZoneId</c>, ADR 0002),
/// so a rota survives daylight-saving changes unchanged. A day may hold several rows for split shifts; rows of one
/// person on one day never overlap (<see cref="WorkingHoursRules"/> plus a unique index on tenant, staff, day, start).
/// </summary>
public class WorkingHours : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid StaffMemberId { get; set; }

    public IsoDayOfWeek Day { get; set; }

    public LocalTime Start { get; set; }

    /// <summary>Exclusive; strictly after <see cref="Start"/> on the same day (no overnight shifts in M1).</summary>
    public LocalTime End { get; set; }

    public int SortOrder { get; set; }

    public TimeRange Range => new(Start, End);
}