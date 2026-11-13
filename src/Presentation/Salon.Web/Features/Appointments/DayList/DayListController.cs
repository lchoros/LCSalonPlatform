using System.Security.Claims;

using Microsoft.AspNetCore.Mvc;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.DayList;

/// <summary>
/// The admin day list at <c>/admin/appointments?date=YYYY-MM-DD</c> (today in the salon's zone by default) and the status
/// buttons on each card, <c>POST /admin/appointments/{id}/{confirm|arrived|complete|no-show|cancel}</c>. Tenant only, for
/// signed-in users with <see cref="Permissions.BookingsManage"/> and <see cref="Permissions.ClientsView"/>: the board
/// shows client names, which the Staff role may not see (ADR 0009, ADR 0011). A successful move redirects back to the
/// day with a confirmation; a move the state machine refuses shows the day again with a message (ADR 0012).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments")]
public sealed class DayListController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string ListPath = "/admin/appointments";

    /// <summary>The fee page of a visit (ADR 0022); the Payments module owns the route, the day list only sends people there.</summary>
    public const string FeePath = "/admin/payments/fees";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? date, CancellationToken cancellationToken)
    {
        // An unparsable date falls back to today rather than failing the page a receptionist opens from a bookmark.
        var day = date is { Length: > 0 } && LocalDatePattern.Iso.Parse(date).Success ? LocalDatePattern.Iso.Parse(date).Value : (LocalDate?)null;
        var board = await dispatcher.SendAsync(new DayListQuery(day), cancellationToken);
        return View(new DayListViewModel(board, TakeFlash(), null));
    }

    [HttpPost("{id:guid}/{transition}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStatus(Guid id, string transition, CancellationToken cancellationToken)
    {
        if (!AppointmentTransitions.TryParse(transition, out var move))
        {
            return NotFound();
        }

        ChangeStatusResult result;
        try
        {
            result = await dispatcher.SendAsync(new ChangeStatusCommand(id, move, CurrentUserId()), cancellationToken);
        }
        catch (AppointmentNotFoundException)
        {
            // Another salon's id looks the same as a missing one (tenant filter).
            return NotFound();
        }

        if (result.Succeeded)
        {
            SetFlash(localizer.Format("Appointments.Day.StatusChanged", localizer[$"Appointments.Status.{result.To}"]));
            // A fee may apply (ADR 0022): straight to its preview, where a person decides. The path is built from the route's Guid.
            return Redirect(result.FeeProposed ? $"{FeePath}/{id}" : DayPath(result.Day));
        }

        var board = await dispatcher.SendAsync(new DayListQuery(result.Day), cancellationToken);
        var error = localizer.Format("Appointments.Day.TransitionNotAllowed", localizer[$"Appointments.Status.{result.From}"], localizer[$"Appointments.Status.{result.To}"]);
        return View("Index", new DayListViewModel(board, null, error));
    }

    public static string DayPath(LocalDate day) => $"{ListPath}?date={LocalDatePattern.Iso.Format(day)}";

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}