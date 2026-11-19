using Microsoft.AspNetCore.Mvc;
using Salon.Core.Identity;
using Salon.Services.Settings;

namespace Salon.Web.Components;

/// <summary>
/// "Your salon is not set up yet" with the way to the getting started page (ADR 0072), shown on the day list and the
/// calendar to the owner only: nobody else could open the page it leads to. Renders nothing once every required step
/// is done, so a set-up salon never sees it. A view component, not a slice: two appointment slices show it and none
/// may reference another.
/// </summary>
public sealed class SetupNoteViewComponent(ISetupProgressService progress) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!HttpContext.User.HasClaim(UserClaimTypes.Permission, Permissions.SettingsManage))
        {
            return Content(string.Empty);
        }

        var state = await progress.GetAsync(HttpContext.RequestAborted);
        return state.AllDone ? Content(string.Empty) : View(state);
    }
}