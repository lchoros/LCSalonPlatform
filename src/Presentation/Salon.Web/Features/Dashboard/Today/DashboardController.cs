using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Formatting;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Dashboard.Today;

/// <summary>
/// <c>/admin</c> (ADR 0084): today at a glance, the first page after staff sign in. Today's visits by what became of
/// them and the next few to come, how full each person's day is, the money of the day and the everyday places one tap
/// away. Read only. Tenant only, for anybody with <see cref="Permissions.BookingsManage"/>, which every role has; the
/// parts that name clients need <see cref="Permissions.ClientsView"/> as the diary does, and the money needs what the
/// till needs (<see cref="Permissions.PaymentsRecord"/> and <see cref="Permissions.ClientsView"/>). A part the role
/// may not open is not read and not shown.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[Route("admin")]
public sealed class DashboardController(IDispatcher dispatcher) : SalonControllerBase
{
    public const string Path = "/admin";

    // The pages the dashboard leads to; each is owned by its own slice and linked by address, because a slice never
    // references another slice (ADR 0043: addresses are built here, not in the view).
    public const string DayListPath = "/admin/appointments";
    public const string BookPath = "/admin/appointments/new";
    public const string CalendarPath = "/admin/appointments/calendar";
    public const string WaitlistPath = "/admin/appointments/waitlist";
    public const string TillPath = "/admin/payments/cash-up";
    public const string GettingStartedPath = "/admin/settings/getting-started";

    /// <summary>The card of one visit; the Calendar slice owns the route.</summary>
    public static string CardPath(Guid appointmentId) => $"{CalendarPath}/{appointmentId}";

    /// <summary>Where the payment of one visit is taken; the Checkout slice owns the route.</summary>
    public static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    /// <summary>The report of one day, which lists every completed visit nobody took payment for (ADR 0026).</summary>
    public static string ReportsPath(LocalDate day) => $"/admin/reports?from={day.ToIso()}&to={day.ToIso()}";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var seesClients = Has(Permissions.ClientsView);
        var query = new DashboardQuery(
            SeesClients: seesClients,
            SeesTill: seesClients && Has(Permissions.PaymentsRecord),
            SeesReports: seesClients && Has(Permissions.ReportsView),
            ManagesSettings: Has(Permissions.SettingsManage));
        return View(await dispatcher.SendAsync(query, cancellationToken));
    }

    private bool Has(string permission) => User.HasClaim(UserClaimTypes.Permission, permission);
}