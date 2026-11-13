using NodaTime;

using Salon.Core.Cqrs;
using Salon.Services.Booking;

namespace Salon.Web.Features.Booking.Reschedule;

/// <summary>
/// The "change the time" page behind a client's own link (ADR 0021): the booking as it is, and the times it could move
/// to. <paramref name="Staff"/> is null for the person the visit is with now, <see cref="AnyStaff"/> for "anyone", or a
/// staff id.
/// </summary>
public sealed record RescheduleQuery(string? Token, LocalDate? Date, string? Staff) : IQuery<RescheduleOptions>
{
    public const string AnyStaff = "any";
}

/// <summary>Moves the booking behind <paramref name="Token"/>; <paramref name="StaffMemberId"/> null means anyone free at that time.</summary>
public sealed record RescheduleCommand(string? Token, Guid? StaffMemberId, Instant NewStart) : ICommand<PublicReschedule>;

public enum RescheduleState
{
    /// <summary>The booking can be moved; days and times are offered.</summary>
    Choose,

    Moved,

    /// <summary>Cancelled, completed or otherwise no longer active.</summary>
    NotActive,

    /// <summary>The visit has started.</summary>
    TooLate,

    /// <summary>The free-change period of the salon's cancellation policy is over (ADR 0022).</summary>
    InsideNotice,

    /// <summary>Unknown or malformed link.</summary>
    Invalid,
}

/// <param name="Appointment">Null only for <see cref="RescheduleState.Invalid"/>.</param>
/// <param name="SelectedStaffId">Null when "anyone" is selected.</param>
public sealed record RescheduleOptions(
    PublicAppointment? Appointment,
    RescheduleState State,
    string SalonName,
    DateTimeZone Zone,
    IReadOnlyList<RescheduleDay> Days,
    LocalDate SelectedDate,
    IReadOnlyList<RescheduleStaff> Staff,
    Guid? SelectedStaffId,
    IReadOnlyList<RescheduleSlot> Slots)
{
    /// <summary>The policy's notice period in hours, for the <see cref="RescheduleState.InsideNotice"/> message.</summary>
    public int FreeChangeHours { get; init; }

    public static RescheduleOptions Without(PublicAppointment? appointment, RescheduleState state, string salonName, DateTimeZone zone)
        => new(appointment, state, salonName, zone, [], default, [], null, []);
}

public sealed record RescheduleDay(LocalDate Date, bool HasSlots);

public sealed record RescheduleStaff(Guid Id, string DisplayName);

public sealed record RescheduleSlot(Instant Start, LocalTime LocalStart);