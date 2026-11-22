using Microsoft.AspNetCore.Mvc;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>
/// <c>/services</c> was the price list beside the booking page. The two showed the same services, so there is one
/// page now (ADR 0094, stage 4): the old address leads to <c>/book</c>, which carries the prices, the durations and
/// the "from" price where staff charge differently. Not a permanent redirect, so the choice can be taken back.
/// </summary>
[RequireTenant]
[Route("services")]
public sealed class ListServicesController : SalonControllerBase
{
    [HttpGet("")]
    public IActionResult Index() => Redirect(PublicBookingDefaults.BookPath);
}