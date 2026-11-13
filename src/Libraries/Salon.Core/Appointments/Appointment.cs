using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Appointments;

/// <summary>
/// One booked visit (glossary: "Wizyta"), ADR 0012. Tenant-owned. <see cref="Start"/> and <see cref="End"/> are instants;
/// the salon's zone applies only when they are shown. Price and duration are copied from the service at booking time so
/// later catalogue edits do not rewrite history. <see cref="Status"/> changes only through
/// <see cref="AppointmentStateMachine"/>, which also produces the <see cref="AppointmentStatusChange"/> row to keep.
/// Postgres refuses two active appointments of one staff member that overlap (exclusion constraint, see the Appointments
/// migration), whatever code path inserts them.
/// </summary>
public class Appointment : BaseEntity, ITenantEntity
{
    public const int NotesMaxLength = 2000;
    public const int CancellationReasonMaxLength = 500;
    public const int CurrencyLength = 3;
    public const int PublicTokenMaxLength = 64;

    public Guid TenantId { get; set; }

    /// <summary>The client (ADR 0011); a foreign key with <c>Restrict</c>, so a client with history is deactivated rather than deleted.</summary>
    public Guid ClientId { get; set; }

    public Guid StaffMemberId { get; set; }

    public Guid ServiceId { get; set; }

    public Instant Start { get; set; }

    /// <summary>Exclusive; <see cref="Start"/> plus <see cref="DurationMinutes"/>. Buffers are not part of the booked range.</summary>
    public Instant End { get; set; }

    public AppointmentStatus Status { get; set; } = AppointmentStatus.Booked;

    public AppointmentSource Source { get; set; }

    /// <summary>Operational notes ("wants the same colour as last time"); never health data.</summary>
    public string? Notes { get; set; }

    /// <summary>Price of the service when it was booked; the service row may change later.</summary>
    public decimal PriceAmount { get; set; }

    /// <summary>ISO 4217 code of <see cref="PriceAmount"/>.</summary>
    public required string Currency { get; set; }

    /// <summary>
    /// The deposit asked for when the visit was booked (ADR 0023), in <see cref="Currency"/>; 0 means none. A snapshot like
    /// the price: later changes to the rules do not rewrite it. Whether it was received is on the client's account.
    /// </summary>
    public decimal DepositAmount { get; set; }

    /// <summary>Duration of the service when it was booked.</summary>
    public int DurationMinutes { get; set; }

    public Instant CreatedAt { get; set; }

    /// <summary>The login that made the booking in the admin; null for online bookings.</summary>
    public Guid? CreatedByUserId { get; set; }

    public Instant? CancelledAt { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>
    /// Secret of the client's cancel link (ADR 0014, <see cref="Booking.PublicBookingToken"/>); set for online bookings,
    /// null for appointments the salon made in the admin. Unique when present.
    /// </summary>
    public string? PublicToken { get; set; }

    public Money Price => new(PriceAmount, Currency);

    /// <summary>The booked range, half-open.</summary>
    public Interval Interval => new(Start, End);
}