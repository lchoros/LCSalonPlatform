using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Notifications;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Api;

namespace Salon.Web.Features.Api.Booking.ShowVisit;

/// <summary>
/// <c>GET /api/v1/booking/visits/{appointmentId}</c> (ADR 0097): the confirmation of an online booking, as JSON, with the
/// client's own link to manage it on the web. The id is a UUIDv7 and names an online booking of this salon or nothing.
/// </summary>
[Route(BookingApi.Root)]
public sealed class VisitsApiController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IPublicUrlBuilder urls) : BookingApiControllerBase
{
    [HttpGet("visits/{appointmentId:guid}")]
    public async Task<ActionResult<BookedVisit>> Show(Guid appointmentId, CancellationToken cancellationToken)
    {
        var visit = await dispatcher.SendAsync(new BookedVisitQuery(appointmentId), cancellationToken);
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        if (visit is null || tenant is null)
        {
            return Missing();
        }

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId) ?? DateTimeZone.Utc;
        return Ok(BookedVisitMapper.Map(visit, zone, urls.CancelBookingUrl(tenant.Slug, visit.PublicToken)));
    }
}