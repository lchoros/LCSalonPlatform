using Microsoft.AspNetCore.Mvc;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.Confirmation;

/// <summary>
/// <c>/book/done/{appointmentId}</c> (ADR 0014): service, person, time in the salon's zone, salon name and the cancel
/// link. Only online bookings have this page; the id is a UUIDv7 (74 random bits), the cancel link a 256-bit token.
/// </summary>
[RequireTenant]
[Route("book/done")]
public sealed class ConfirmationController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Done(Guid id, CancellationToken cancellationToken)
    {
        var appointment = await dispatcher.SendAsync(new ConfirmationQuery(id), cancellationToken);
        if (appointment is null)
        {
            return NotFound();
        }

        // [RequireTenant] has already turned a tenant-less request into a 404; instants are shown in the salon's zone (rule 5).
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var zone = (tenant is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId)) ?? DateTimeZone.Utc;
        return View(new ConfirmationViewModel(appointment, tenant?.Name ?? string.Empty, zone, PublicBookingDefaults.CancelPath(appointment.PublicToken)));
    }
}

public sealed record ConfirmationViewModel(PublicAppointment Appointment, string SalonName, DateTimeZone Zone, string CancelPath);
