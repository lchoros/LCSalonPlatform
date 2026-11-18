using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.Waitlist;

/// <summary>
/// The waitlist at <c>/admin/appointments/waitlist?page=</c> (ADR 0041): the clients who want a visit the diary has no
/// room for yet, the one who asked first on top, each with a way to book them and a button to take them off the list.
/// Tenant only, for signed-in users with <see cref="Permissions.BookingsManage"/> and
/// <see cref="Permissions.ClientsView"/>, like the day list: the rows name clients and show their phone numbers.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments/waitlist")]
public sealed class WaitlistController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/appointments/waitlist";

    /// <summary>The day list; the DayList slice owns the route.</summary>
    public const string DayListPath = "/admin/appointments";

    /// <summary>The booking form; the BookForClient slice owns the route.</summary>
    public const string BookPath = "/admin/appointments/new";

    [HttpGet("")]
    public async Task<IActionResult> Index(int? page, CancellationToken cancellationToken)
    {
        var waiting = await dispatcher.SendAsync(new WaitlistQuery(page), cancellationToken);
        return View(new WaitlistViewModel(waiting, PagerModel.For(waiting, PageAddress), TakeFlash()));
    }

    [HttpPost("{id:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid id, int? page, CancellationToken cancellationToken)
    {
        if (!await dispatcher.SendAsync(new RemoveFromWaitlistCommand(id, CurrentUserId()), cancellationToken))
        {
            // Another salon's id, a missing one and one already closed all look the same.
            return NotFound();
        }

        SetFlash(localizer["Appointments.Waitlist.Removed"]);
        return Redirect(PageAddress(page ?? 1));
    }

    public static string PageAddress(int page)
        => ListUrl.Build(Path, (ListUrl.PageParameter, Math.Max(1, page).ToString(CultureInfo.InvariantCulture)));

    public static string RemovePath(Guid id, int page)
        => ListUrl.Build($"{Path}/{id}/remove", (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Where the desk lets go of a time kept for a waiting client (ADR 0065); the ReleaseHeldTime slice owns the route.</summary>
    public static string ReleasePath(Guid offerId) => $"{Path}/held/{offerId}/release";

    /// <summary>The booking form for a waiting client: who, what, with whom and the first day they can come.</summary>
    public static string BookAddress(WaitlistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return ListUrl.Build(
            BookPath,
            ("clientId", item.ClientId.ToString()),
            ("serviceId", item.ServiceId.ToString()),
            ("staffId", item.StaffMemberId?.ToString()),
            ("date", LocalDatePattern.Iso.Format(item.BookingDay)));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}