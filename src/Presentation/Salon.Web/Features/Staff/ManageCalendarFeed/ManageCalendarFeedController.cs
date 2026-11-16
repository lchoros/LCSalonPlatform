using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.ManageCalendarFeed;

/// <summary>
/// A staff member's calendar address at <c>/admin/staff/{id}/calendar-feed</c> (ADR 0038), for signed-in users with
/// <see cref="Permissions.StaffManage"/>: whether it is on, the address to paste into Google or Apple Calendar, a
/// button for a new address (the old one stops working) and one to switch it off. The address is a secret: anyone who
/// has it can read that person's visits, so the page says so and never shows it in a list.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff/{id:guid}/calendar-feed")]
public sealed class ManageCalendarFeedController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The staff list; the ListStaff slice owns the route.</summary>
    public const string StaffPath = "/admin/staff";

    /// <summary>The file a calendar app reads; the CalendarFeedFile slice owns the route.</summary>
    public const string FilePath = "/calendar";

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var state = await dispatcher.SendAsync(new CalendarFeedStateQuery(id), cancellationToken);
        if (state is null)
        {
            return NotFound();
        }

        // The address is built from this request's own scheme and host: the salon's domain, as the owner sees it.
        var address = state.Token is { } token ? $"{Request.Scheme}://{Request.Host}{FilePath}/{token}.ics" : null;
        return View(new ManageCalendarFeedViewModel(state, address, TakeFlash()));
    }

    [HttpPost("new-address")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewAddress(Guid id, CancellationToken cancellationToken)
    {
        var before = await dispatcher.SendAsync(new CalendarFeedStateQuery(id), cancellationToken);
        if (before is null || !await dispatcher.SendAsync(new NewCalendarFeedAddressCommand(id), cancellationToken))
        {
            return NotFound();
        }

        SetFlash(localizer[before.IsOn ? "Staff.CalendarFeed.Replaced" : "Staff.CalendarFeed.SwitchedOn"]);
        return Redirect(PagePath(id));
    }

    [HttpPost("off")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Off(Guid id, CancellationToken cancellationToken)
    {
        if (!await dispatcher.SendAsync(new SwitchCalendarFeedOffCommand(id), cancellationToken))
        {
            return NotFound();
        }

        SetFlash(localizer["Staff.CalendarFeed.SwitchedOff"]);
        return Redirect(PagePath(id));
    }

    public static string PagePath(Guid id) => $"{StaffPath}/{id}/calendar-feed";
}

/// <param name="Address">The full address to paste into a calendar app; null while it is switched off.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ManageCalendarFeedViewModel(CalendarFeedState State, string? Address, string? Flash);