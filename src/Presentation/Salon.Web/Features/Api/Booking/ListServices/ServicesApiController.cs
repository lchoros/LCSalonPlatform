using Microsoft.AspNetCore.Mvc;
using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Web.Framework.Api;

namespace Salon.Web.Features.Api.Booking.ListServices;

/// <summary><c>GET /api/v1/booking/services</c> (ADR 0097): what <c>/book</c> lists, as JSON.</summary>
[Route(BookingApi.Root)]
public sealed class ServicesApiController(IDispatcher dispatcher) : BookingApiControllerBase
{
    [HttpGet("services")]
    public async Task<ActionResult<ServiceList>> List(CancellationToken cancellationToken)
        => Ok(await dispatcher.SendAsync(new BookableServicesQuery(), cancellationToken));
}