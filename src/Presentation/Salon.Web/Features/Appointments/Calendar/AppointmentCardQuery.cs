using NodaTime;

using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Domain;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// One appointment opened from the calendar (ADR 0016): its details, the status moves the state machine allows and the
/// move form with the free times of <paramref name="MoveStaffId"/> on <paramref name="MoveDate"/> (defaults: the
/// appointment's own person and day), the time it holds now left out. A query: no transaction, tenant required. Throws
/// <see cref="Salon.Services.Appointments.AppointmentNotFoundException"/> when the salon has no such appointment.
/// </summary>
public sealed record AppointmentCardQuery(Guid AppointmentId, Guid? MoveStaffId, LocalDate? MoveDate) : IQuery<AppointmentCard>;

/// <param name="Day">The appointment's local day, for the back link.</param>
/// <param name="Actions">The status buttons, in order; empty for a finished or cancelled visit.</param>
/// <param name="IsActive">True when the visit holds a slot and can be moved.</param>
/// <param name="Staff">Bookable staff for the move select (plus the visit's own person, even if no longer bookable).</param>
/// <param name="Slots">Free starts for the move selection; empty when nothing is free or the visit cannot be moved.</param>
/// <param name="ClientOwes">Open fees on the client's account (ADR 0022), so the desk sees them when the client is back; null when nothing is owed.</param>
public sealed record AppointmentCard(
    Guid Id,
    LocalDate Day,
    LocalTime Start,
    LocalTime End,
    string ClientName,
    string ServiceName,
    string StaffName,
    Guid StaffMemberId,
    string Colour,
    AppointmentStatus Status,
    Money Price,
    string? Notes,
    IReadOnlyList<CardAction> Actions,
    bool IsActive,
    IReadOnlyList<CalendarStaff> Staff,
    Guid MoveStaffId,
    LocalDate MoveDate,
    IReadOnlyList<MoveSlot> Slots,
    Guid ClientId = default,
    Money? ClientOwes = null);

/// <summary>A status button: the URL segment of the day list's <c>POST admin/appointments/{id}/{segment}</c> route and the label key.</summary>
public sealed record CardAction(string Segment, AppointmentStatus Target, string LabelKey);

public static class CardActions
{
    /// <summary>In button order; the day list owns the routes (ADR 0012), the calendar only links to them.</summary>
    public static IReadOnlyList<CardAction> All { get; } =
    [
        new("confirm", AppointmentStatus.Confirmed, "Appointments.Action.Confirm"),
        new("arrived", AppointmentStatus.Arrived, "Appointments.Action.Arrived"),
        new("complete", AppointmentStatus.Completed, "Appointments.Action.Complete"),
        new("no-show", AppointmentStatus.NoShow, "Appointments.Action.NoShow"),
        new("cancel", AppointmentStatus.CancelledBySalon, "Appointments.Action.Cancel"),
    ];

    public static IReadOnlyList<CardAction> For(AppointmentStatus status)
        => All.Where(a => AppointmentStateMachine.CanTransition(status, a.Target)).ToArray();
}

/// <param name="Start">The instant the slot form posts; <see cref="LocalStart"/> is what the button shows.</param>
public sealed record MoveSlot(Instant Start, LocalTime LocalStart);

/// <param name="Error">A refused move, already localized.</param>
public sealed record AppointmentCardViewModel(AppointmentCard Card, string? Error);