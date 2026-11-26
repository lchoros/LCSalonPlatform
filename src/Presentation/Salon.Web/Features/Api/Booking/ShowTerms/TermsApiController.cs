using Microsoft.AspNetCore.Mvc;
using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Web.Framework.Api;

namespace Salon.Web.Features.Api.Booking.ShowTerms;

/// <summary><c>GET /api/v1/booking/terms</c> (ADR 0097): the notices of the details page, as JSON.</summary>
[Route(BookingApi.Root)]
public sealed class TermsApiController(IDispatcher dispatcher) : BookingApiControllerBase
{
    [HttpGet("terms")]
    public async Task<ActionResult<BookingTerms>> Show(CancellationToken cancellationToken)
        => Ok(await dispatcher.SendAsync(new BookingTermsQuery(), cancellationToken));
}