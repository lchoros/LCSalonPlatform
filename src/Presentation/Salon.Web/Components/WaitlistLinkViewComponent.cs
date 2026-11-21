using Microsoft.AspNetCore.Mvc;
using Salon.Services.Appointments;

namespace Salon.Web.Components;

/// <summary>
/// The way to the waitlist with how many clients are waiting (ADR 0041), shown beside the buttons of the day list and
/// the calendar. Always there, so the desk can open an empty list too; the number appears only when somebody waits.
/// A view component, not a slice: two appointment slices show it and none may reference another. One count query.
/// </summary>
public sealed class WaitlistLinkViewComponent(IWaitlistService waitlist) : ViewComponent
{
    /// <param name="onlyWhenWaiting">
    /// On the calendar (ADR 0092) the waiting list is a tab, so the link is drawn only when it has something to say:
    /// that somebody is waiting.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(bool onlyWhenWaiting = false)
    {
        var waiting = await waitlist.CountWaitingAsync(HttpContext.RequestAborted);
        return onlyWhenWaiting && waiting == 0 ? Content(string.Empty) : View(waiting);
    }
}