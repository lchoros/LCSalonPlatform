using NodaTime;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Staff;
using Salon.Services.Appointments;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// The server-rendered admin calendar (ADR 0016): a day (one column per bookable staff member, or one person when
/// filtered) or a Monday-to-Sunday week of one person, as a 15-minute timetable with every active appointment placed by
/// <see cref="CalendarLayout"/>. Times are in the salon's zone. A query: no transaction, tenant required.
/// </summary>
/// <param name="Date">The day shown (day view) or any day of the week shown (week view); null means today in the salon's zone.</param>
/// <param name="StaffMemberId">Day view: the filter, null for everyone. Week view: whose week; null means the first bookable person.</param>
public sealed record CalendarQuery(CalendarView View, LocalDate? Date, Guid? StaffMemberId) : IQuery<CalendarBoard>;

public enum CalendarView
{
    Day,
    Week,
}

/// <param name="Day">The anchor day (the day shown, or the day inside the week that was asked for).</param>
/// <param name="From">First day on the board (the Monday in week view).</param>
/// <param name="To">Last day on the board (the Sunday in week view).</param>
/// <param name="Staff">Bookable staff in display order, plus anyone else with a visit on the board; the chips.</param>
/// <param name="SelectedStaffId">The chip that is on; null is "All" (day view only).</param>
/// <param name="Grid">The rows to draw, shared by every column.</param>
/// <param name="Columns">One per staff member (day view) or per day (week view), in order.</param>
public sealed record CalendarBoard(
    CalendarView View,
    LocalDate Day,
    LocalDate From,
    LocalDate To,
    IReadOnlyList<CalendarStaff> Staff,
    Guid? SelectedStaffId,
    GridRange Grid,
    IReadOnlyList<CalendarColumn> Columns);

public sealed record CalendarStaff(Guid Id, string DisplayName, string Colour);

/// <param name="Date">The local day of the column.</param>
/// <param name="WorkingRanges">The person's rota for that weekday; rows outside are shaded.</param>
/// <param name="Blocks">Active appointments, already positioned, in start order.</param>
public sealed record CalendarColumn(LocalDate Date, CalendarStaff Staff, IReadOnlyList<TimeRange> WorkingRanges, IReadOnlyList<CalendarBlock> Blocks);

/// <summary>One appointment on the grid; <see cref="Row"/>, <see cref="Span"/>, <see cref="Lane"/> and <see cref="Lanes"/> come from <see cref="CalendarLayout.Position"/>.</summary>
public sealed record CalendarBlock(
    Guid Id,
    LocalTime Start,
    LocalTime End,
    string ClientFirstName,
    string ServiceName,
    AppointmentStatus Status,
    int Row,
    int Span,
    int Lane,
    int Lanes);

/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record CalendarViewModel(CalendarBoard Board, string? Flash);