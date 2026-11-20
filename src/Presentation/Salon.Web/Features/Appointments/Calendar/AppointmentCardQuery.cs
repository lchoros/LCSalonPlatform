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
public sealed record AppointmentCardQuery(Guid AppointmentId, Guid? MoveStaffId, LocalDate? MoveDate, Instant? MoveStart = null) : IQuery<AppointmentCard>;

/// <param name="Day">The appointment's local day, for the back link.</param>
/// <param name="Actions">The status buttons, in order; empty for a finished or cancelled visit.</param>
/// <param name="IsActive">True when the visit holds a slot and can be moved.</param>
/// <param name="Staff">Bookable staff for the move select (plus the visit's own person, even if no longer bookable).</param>
/// <param name="Slots">Free starts for the move selection; empty when nothing is free or the visit cannot be moved.</param>
/// <param name="ClientOwes">Open fees on the client's account (ADR 0022), so the desk sees them when the client is back; null when nothing is owed.</param>
/// <param name="DepositDue">The deposit the booking asks for (ADR 0023); null when it asks for none.</param>
/// <param name="DepositHeld">The deposit the salon received for this visit and still holds; null when none.</param>
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
    Money? ClientOwes = null,
    Money? DepositDue = null,
    Money? DepositHeld = null)
{
    /// <summary>Today in the salon's zone: the earliest day the move form offers (a past day never has free times).</summary>
    public LocalDate Today { get; init; }

    /// <summary>The free time the desk tapped, when it is one of <see cref="Slots"/>: the move happens only on the confirm button (ADR 0086).</summary>
    public MoveSlot? Chosen { get; init; }

    /// <summary>Everything that happened to the visit, oldest first (ADR 0091).</summary>
    public IReadOnlyList<VisitHistoryLine> History { get; init; } = [];
}

/// <summary>One line of a visit's history, in the salon's time.</summary>
/// <param name="By">The login that did it; null when the client did it online or a job did.</param>
/// <param name="Reason">The words given with a cancellation; null otherwise.</param>
/// <param name="From">For a move recorded since ADR 0091: where the visit was.</param>
/// <param name="To">For a move recorded since ADR 0091: where it went.</param>
public sealed record VisitHistoryLine(LocalDateTime At, VisitHistoryKind Kind, string? By, string? Reason, VisitPlace? From, VisitPlace? To);

/// <summary>A time and the person it was with; the name is null when that person is no longer on record.</summary>
public sealed record VisitPlace(LocalDateTime At, string? StaffName);

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