using NodaTime;
using Salon.Core.Cqrs;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>
/// Books one of the offered slots for a client from the admin (ADR 0012, <c>Source.Admin</c>). Bound straight from the
/// slot button's form; <see cref="BookForClientValidator"/> holds the rules, the booking service re-checks the slot and
/// snapshots price and duration. Ids are nullable so a blank field is reported in the salon's language instead of MVC's.
/// </summary>
/// <param name="StaffMemberId">The slot's staff member, also when the user chose "any available".</param>
/// <param name="Start">The slot's start instant as ISO-8601 (<c>2026-11-13T10:00:00Z</c>), as the button posts it.</param>
/// <param name="Date">The ISO day the form showed (<c>yyyy-MM-dd</c>), to re-render the same day on failure.</param>
/// <param name="AnyStaff">True when the user chose "any available", so a re-render keeps that choice.</param>
/// <param name="ByUserId">The signed-in user (the <c>sub</c> claim), set by the controller and recorded on the booking.</param>
public sealed record BookForClientCommand(
    Guid? ClientId,
    Guid? ServiceId,
    Guid? StaffMemberId,
    string? Start,
    string? Date,
    bool AnyStaff,
    Guid? ByUserId) : ICommand<BookForClientResult>;

/// <param name="ClientName">For the confirmation.</param>
/// <param name="Start">Local to the salon, for the confirmation and the redirect to that day.</param>
public sealed record BookForClientResult(Guid AppointmentId, string ClientName, LocalDateTime Start);