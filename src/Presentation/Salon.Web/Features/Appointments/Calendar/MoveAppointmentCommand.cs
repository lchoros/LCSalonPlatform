using NodaTime;

using Salon.Core.Cqrs;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// Moves one appointment of the current salon to a slot the card offered (ADR 0016). <see cref="MoveAppointmentValidator"/>
/// holds the input rules; the booking service re-checks the slot, keeps the price and duration snapshot, writes the
/// "Moved" history row and publishes <see cref="Core.Appointments.Events.AppointmentMoved"/>.
/// </summary>
/// <param name="StaffMemberId">The person the slot belongs to.</param>
/// <param name="Start">The slot's start instant as ISO-8601 (<c>2026-11-13T10:00:00Z</c>), as the button posts it.</param>
/// <param name="Date">The ISO day the move form showed (<c>yyyy-MM-dd</c>), to re-render the same day on failure.</param>
/// <param name="ByUserId">The signed-in user (the <c>sub</c> claim), recorded on the history row.</param>
public sealed record MoveAppointmentCommand(Guid AppointmentId, Guid? StaffMemberId, string? Start, string? Date, Guid? ByUserId) : ICommand<MoveAppointmentResult>;

/// <param name="Start">Local to the salon, for the confirmation and the redirect to that day.</param>
public sealed record MoveAppointmentResult(Guid AppointmentId, Guid StaffMemberId, string StaffName, string ClientName, LocalDateTime Start);