using NodaTime;
using Salon.Core.Cqrs;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Moves one appointment of the current salon to a slot the card offered, or to the row a dragged block was dropped
/// on (ADR 0016, ADR 0086). <see cref="MoveAppointmentValidator"/> holds the input rules; the booking service
/// re-checks the slot either way, keeps the price and duration snapshot, writes the "Moved" history row and publishes
/// <see cref="Core.Appointments.Events.AppointmentMoved"/>.
/// </summary>
/// <param name="StaffMemberId">The person the slot belongs to.</param>
/// <param name="Start">The slot's start instant as ISO-8601 (<c>2026-11-13T10:00:00Z</c>), as the card's button posts it; null for a drop.</param>
/// <param name="Date">The ISO day (<c>yyyy-MM-dd</c>): the day the move form showed, or the day of the column a block was dropped on.</param>
/// <param name="ByUserId">The signed-in user (the <c>sub</c> claim), recorded on the history row.</param>
/// <param name="Minute">
/// A drop's minute of the day on the salon's own clock, with <paramref name="Date"/>: the browser names a row, the
/// server turns it into an instant in the salon's zone — no time-zone arithmetic ever runs in a script.
/// </param>
public sealed record MoveAppointmentCommand(Guid AppointmentId, Guid? StaffMemberId, string? Start, string? Date, Guid? ByUserId, int? Minute = null) : ICommand<MoveAppointmentResult>;

/// <param name="Start">Local to the salon, for the confirmation and the redirect to that day.</param>
public sealed record MoveAppointmentResult(Guid AppointmentId, Guid StaffMemberId, string StaffName, string ClientName, LocalDateTime Start);