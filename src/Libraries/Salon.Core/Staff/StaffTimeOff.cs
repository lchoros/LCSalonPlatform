using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Staff;

/// <summary>
/// A planned absence of a staff member (glossary: "Urlop / Nieobecność"), ADR 0015: every day from <see cref="FromDate"/>
/// to <see cref="ToDate"/> inclusive, either whole days (<see cref="Start"/> and <see cref="End"/> both null) or the same
/// local time range on each of those days (both set). Dates and times are local to the salon, like the rota (ADR 0010);
/// the availability engine turns a row into busy intervals per day with the tenant's zone. Entries of one person never
/// overlap (<see cref="StaffTimeOffRules.Overlaps"/>).
/// </summary>
public class StaffTimeOff : BaseEntity, ITenantEntity
{
    public const int ReasonMaxLength = 200;

    public Guid TenantId { get; set; }

    public Guid StaffMemberId { get; set; }

    public LocalDate FromDate { get; set; }

    /// <summary>Inclusive; never before <see cref="FromDate"/>.</summary>
    public LocalDate ToDate { get; set; }

    /// <summary>Null for whole days; otherwise the time the absence starts on each day of the span.</summary>
    public LocalTime? Start { get; set; }

    /// <summary>Null for whole days; otherwise strictly after <see cref="Start"/> on the same day.</summary>
    public LocalTime? End { get; set; }

    /// <summary>Free text for the salon only ("Holiday", "Dentist"); never shown to clients.</summary>
    public string? Reason { get; set; }

    public Instant CreatedAt { get; set; }

    /// <summary>True when the absence covers the whole of each day in the span.</summary>
    public bool IsWholeDay => Start is null && End is null;

    /// <summary>The daily time range of a partial absence; null for whole days.</summary>
    public TimeRange? Range => Start is { } start && End is { } end ? new TimeRange(start, end) : null;
}
