using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.Calendar;

/// <summary>
/// The admin calendar (ADR 0016) at <c>/admin/appointments/calendar?view=day|week&amp;date=YYYY-MM-DD&amp;staff=</c>, the
/// appointment card at <c>/admin/appointments/calendar/{id}</c> and its move form, <c>POST .../{id}/move</c>. Everything
/// is server-rendered: navigation is plain links, the free times are small POST forms. Tenant only, for signed-in users with
/// <see cref="Permissions.BookingsManage"/> and <see cref="Permissions.ClientsView"/> (the grid shows client names,
/// ADR 0009, ADR 0011). A successful move lands on the calendar of the new day with a confirmation; a slot taken meanwhile
/// re-renders the card with a message and fresh times (ADR 0012).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments/calendar")]
public sealed class CalendarController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string CalendarPath = "/admin/appointments/calendar";

    /// <summary>The day list, which also owns the status-change routes the card posts to.</summary>
    public const string DayListPath = "/admin/appointments";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? view, string? date, Guid? staff, CancellationToken cancellationToken)
    {
        var board = await dispatcher.SendAsync(new CalendarQuery(ParseView(view), ParseDate(date), staff), cancellationToken);
        return View(new CalendarViewModel(board, TakeFlash()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Card(Guid id, Guid? staffId, string? date, CancellationToken cancellationToken)
    {
        try
        {
            var card = await dispatcher.SendAsync(new AppointmentCardQuery(id, staffId, ParseDate(date)), cancellationToken);
            return View(new AppointmentCardViewModel(card, null));
        }
        catch (AppointmentNotFoundException)
        {
            // Another salon's id looks the same as a missing one (tenant filter).
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/move")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(Guid id, Guid? staffMemberId, string? start, string? date, CancellationToken cancellationToken)
    {
        var command = new MoveAppointmentCommand(id, staffMemberId, start, date, CurrentUserId());
        MoveAppointmentResult result;
        try
        {
            result = await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderCardAgainAsync(command, null, cancellationToken);
        }
        catch (AppointmentNotFoundException)
        {
            return NotFound();
        }
        catch (AppointmentNotActiveException)
        {
            return await RenderCardAgainAsync(command, localizer["Appointments.Calendar.Move.NotActive"], cancellationToken);
        }
        catch (SlotNoLongerAvailableException)
        {
            return await RenderCardAgainAsync(command, localizer["Appointments.Calendar.Move.SlotTaken"], cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The service went inactive or the staff member stopped being bookable between the card and the post.
            return await RenderCardAgainAsync(command, localizer["Appointments.Calendar.Move.Unavailable"], cancellationToken);
        }

        var culture = CultureInfo.CurrentCulture;
        SetFlash(localizer.Format(
            "Appointments.Calendar.Move.Moved",
            result.ClientName,
            result.Start.Date.ToString("d", culture),
            result.Start.TimeOfDay.ToString("t", culture),
            result.StaffName));
        return Redirect(Path(CalendarView.Day, result.Start.Date, result.StaffMemberId));
    }

    /// <summary>The calendar for <paramref name="view"/> on <paramref name="day"/>; <paramref name="staff"/> null is "All" (day) or the first person (week).</summary>
    public static string Path(CalendarView view, LocalDate day, Guid? staff)
        => $"{CalendarPath}?view={ViewSegment(view)}&date={LocalDatePattern.Iso.Format(day)}{StaffQuery(staff)}";

    /// <summary>The calendar for <paramref name="view"/> on today in the salon's zone (the handler decides what "today" is).</summary>
    public static string TodayPath(CalendarView view, Guid? staff) => $"{CalendarPath}?view={ViewSegment(view)}{StaffQuery(staff)}";

    /// <summary>The appointment card, optionally with the move form set to another person and day.</summary>
    public static string CardPath(Guid appointmentId, Guid? staffId = null, LocalDate? date = null)
    {
        var path = $"{CalendarPath}/{appointmentId}";
        var parts = new List<string>();
        if (staffId is { } staff)
        {
            parts.Add($"staffId={staff}");
        }

        if (date is { } day)
        {
            parts.Add($"date={LocalDatePattern.Iso.Format(day)}");
        }

        return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }

    public static string ViewSegment(CalendarView view) => view == CalendarView.Week ? "week" : "day";

    /// <summary>The same card with fresh times for the person and day the form showed.</summary>
    private async Task<IActionResult> RenderCardAgainAsync(MoveAppointmentCommand command, string? error, CancellationToken cancellationToken)
    {
        try
        {
            var card = await dispatcher.SendAsync(new AppointmentCardQuery(command.AppointmentId, command.StaffMemberId, ParseDate(command.Date)), cancellationToken);
            return View("Card", new AppointmentCardViewModel(card, error));
        }
        catch (AppointmentNotFoundException)
        {
            return NotFound();
        }
    }

    private static string StaffQuery(Guid? staff) => staff is { } id ? $"&staff={id}" : string.Empty;

    private static CalendarView ParseView(string? view)
        => string.Equals(view, "week", StringComparison.OrdinalIgnoreCase) ? CalendarView.Week : CalendarView.Day;

    /// <summary>An absent or unparsable date means today in the salon's zone (the handler decides what "today" is).</summary>
    private static LocalDate? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        var result = LocalDatePattern.Iso.Parse(iso.Trim());
        return result.Success ? result.Value : null;
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}