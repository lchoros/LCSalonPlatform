using System.Net.Mime;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Notifications;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Api;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Features.Api.Booking.CheckAvailability;

/// <summary>
/// <c>POST /api/v1/booking/availability</c> (ADR 0097): the time screen for the visit as chosen so far — the days, the
/// times of one day, each service with who does it, and whether the client may go on. It sends the time page's own
/// query (<c>/book/{service}?staff=&amp;also=&amp;with=&amp;date=&amp;from=&amp;start=</c>), so both say the same.
/// A first service this salon does not offer is a 404; a further one that is gone drops out of the answer, as on the page.
/// </summary>
[Route(BookingApi.Root)]
public sealed class AvailabilityApiController(
    IDispatcher dispatcher,
    IValidator<AvailabilityRequest> validator,
    IVisitBasket basket,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IPublicUrlBuilder urls) : BookingApiControllerBase
{
    [HttpPost("availability")]
    [Consumes(MediaTypeNames.Application.Json)]
    public async Task<ActionResult<Availability>> Check([FromBody] AvailabilityRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || !ModelState.IsValid)
        {
            return Unreadable(ModelState);
        }

        var check = await validator.ValidateAsync(request, cancellationToken);
        if (!check.IsValid)
        {
            return Invalid(check.Errors);
        }

        var result = await dispatcher.SendAsync(AvailabilityMapper.ToQuery(request), cancellationToken);
        if (result is null)
        {
            return Missing();
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        return Ok(AvailabilityMapper.Map(result, basket.IsOpen, path => tenant is null ? path : urls.SalonUrl(tenant.Slug, path)));
    }
}