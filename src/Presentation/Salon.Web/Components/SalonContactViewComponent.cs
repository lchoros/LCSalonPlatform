using Microsoft.AspNetCore.Mvc;
using Salon.Services.Locations;

namespace Salon.Web.Components;

/// <summary>
/// "Where to find us" on the public booking pages (ADR 0027): the address of the salon's main location with a maps
/// link, its phone number and its email address. Renders nothing while the salon has not entered an address, so a page
/// never shows an empty box. A view component, not a slice: several booking slices show it and none may reference
/// another.
/// </summary>
public sealed class SalonContactViewComponent(ILocationService locations) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var location = await locations.GetMainAsync(HttpContext.RequestAborted);
        return location is null ? Content(string.Empty) : View(location);
    }
}