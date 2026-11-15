using Microsoft.AspNetCore.Mvc;
using Salon.Services.Appointments;

namespace Salon.Web.Components;

/// <summary>
/// "3 earlier visits have no outcome yet" with a link to the list (ADR 0034), shown on the day list and the calendar.
/// Renders nothing while every past visit has an outcome, so a tidy salon never sees it. A view component, not a
/// slice: two appointment slices show it and none may reference another. One count query per page.
/// </summary>
public sealed class VisitsWithoutOutcomeViewComponent(IVisitOutcomeService outcomes) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var count = await outcomes.CountMissingAsync(HttpContext.RequestAborted);
        return count == 0 ? Content(string.Empty) : View(count);
    }
}