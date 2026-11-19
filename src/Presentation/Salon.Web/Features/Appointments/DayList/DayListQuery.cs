using System.Globalization;
using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Services.Formatting;

namespace Salon.Web.Features.Appointments.DayList;

/// <summary>
/// One day of the current salon: every bookable staff member with that day's active appointments (ADR 0012), names
/// resolved through the tenant filter and times already in the salon's zone. <paramref name="Date"/> null means today in
/// that zone. A query: no transaction, tenant required.
/// </summary>
public sealed record DayListQuery(LocalDate? Date) : IQuery<DayBoard>;

/// <param name="Day">The local day shown; prev/next links are built from it.</param>
/// <param name="Staff">Bookable staff in display order, plus anyone else who has an appointment that day; empty shows the "no staff" state.</param>
public sealed record DayBoard(LocalDate Day, IReadOnlyList<StaffDay> Staff);

/// <summary>One column of the board.</summary>
/// <param name="Kept">
/// Times of this person kept for a waiting client right now (ADR 0069), in start order; shown above the visits.
/// </param>
public sealed record StaffDay(Guid StaffMemberId, string DisplayName, string Colour, IReadOnlyList<DayAppointment> Appointments, IReadOnlyList<DayKeptTime> Kept);

/// <summary>
/// A freed time kept off the booking pages and the desk's form for the client who was told about it (ADR 0065, ADR
/// 0069). <see cref="Start"/>, <see cref="End"/> and <see cref="HeldUntil"/> are local to the salon.
/// </summary>
/// <param name="EndsToday">Whether the hold ends on the salon's today; otherwise its day is shown with the time.</param>
public sealed record DayKeptTime(Guid OfferId, LocalTime Start, LocalTime End, LocalDateTime HeldUntil, bool EndsToday, string ClientName, string ServiceName)
{
    /// <summary>"15:30" when the hold ends today, "Thu 20 Nov, 15:30" otherwise.</summary>
    public string Until(CultureInfo culture)
        => EndsToday ? HeldUntil.TimeOfDay.ToShortTime(culture) : $"{HeldUntil.Date.ToString("ddd d MMM", culture)}, {HeldUntil.TimeOfDay.ToShortTime(culture)}";
}

/// <summary>One card in a column; <see cref="Start"/> and <see cref="End"/> are local to the salon.</summary>
public sealed record DayAppointment(Guid Id, LocalTime Start, LocalTime End, string ClientName, string ServiceName, AppointmentStatus Status)
{
    /// <summary>The moves the state machine allows from here, in the order the buttons appear.</summary>
    public IReadOnlyList<AppointmentTransition> Actions { get; } =
        AppointmentTransitions.All.Where(t => AppointmentStateMachine.CanTransition(Status, AppointmentTransitions.Target(t))).ToArray();
}

/// <param name="Board">The day.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
/// <param name="Error">A refused status change, already localized, shown above the board.</param>
public sealed record DayListViewModel(DayBoard Board, string? Flash, string? Error);