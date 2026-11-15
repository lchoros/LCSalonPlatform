using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.WithoutOutcome;

/// <summary>
/// Past visits nobody marked, at <c>/admin/appointments/without-outcome?page=</c>, and the two buttons on each row,
/// <c>POST /admin/appointments/without-outcome/{id}/{came|did-not-come}</c> (ADR 0034). Tenant only, for signed-in
/// users with <see cref="Permissions.BookingsManage"/> and <see cref="Permissions.ClientsView"/>, like the day list:
/// the rows show client names (ADR 0009, ADR 0011). A recorded outcome returns to the same page of the list, or to
/// the fee preview when the salon's policy allows a fee for a no-show (ADR 0022).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments/without-outcome")]
public sealed class WithoutOutcomeController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/appointments/without-outcome";

    /// <summary>The day list; the Appointments day slice owns the route, this page only links back to it.</summary>
    public const string DayListPath = "/admin/appointments";

    /// <summary>A visit's card; the calendar slice owns the route.</summary>
    public const string CardPath = "/admin/appointments/calendar";

    /// <summary>The fee page of a visit (ADR 0022); the Payments module owns the route.</summary>
    public const string FeePath = "/admin/payments/fees";

    [HttpGet("")]
    public async Task<IActionResult> Index(int? page, CancellationToken cancellationToken)
    {
        var visits = await dispatcher.SendAsync(new WithoutOutcomeQuery(page), cancellationToken);
        return View(new WithoutOutcomeViewModel(visits, PagerModel.For(visits, PageAddress), TakeFlash()));
    }

    [HttpPost("{id:guid}/{outcome}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record(Guid id, string outcome, int? page, CancellationToken cancellationToken)
    {
        if (!VisitOutcomeSegments.TryParse(outcome, out var chosen))
        {
            return NotFound();
        }

        RecordOutcomeResult result;
        try
        {
            result = await dispatcher.SendAsync(new RecordOutcomeCommand(id, chosen, CurrentUserId()), cancellationToken);
        }
        catch (AppointmentNotFoundException)
        {
            // Another salon's id looks the same as a missing one (tenant filter).
            return NotFound();
        }

        if (!result.Recorded)
        {
            SetFlash(localizer["Appointments.Outcome.NotRecorded"]);
            return Redirect(PageAddress(page ?? 1));
        }

        SetFlash(localizer[$"Appointments.Outcome.Recorded.{chosen}"]);
        // The paths are built from the route's Guid and a number, never from text the request supplied.
        return Redirect(result.FeeProposed ? $"{FeePath}/{id}" : PageAddress(page ?? 1));
    }

    public static string PageAddress(int page)
        => ListUrl.Build(Path, (ListUrl.PageParameter, Math.Max(1, page).ToString(CultureInfo.InvariantCulture)));

    public static string RecordPath(Guid id, VisitOutcome outcome, int page)
        => ListUrl.Build($"{Path}/{id}/{VisitOutcomeSegments.Segment(outcome)}", (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}