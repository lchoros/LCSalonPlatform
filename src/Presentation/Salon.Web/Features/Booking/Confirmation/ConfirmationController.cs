using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.Confirmation;

/// <summary>
/// <c>/book/done/{appointmentId}</c> (ADR 0014): service, person, time in the salon's zone, salon name and the cancel
/// link. Only online bookings have this page; the id is a UUIDv7 (74 random bits), the cancel link a 256-bit token.
/// With a payment gateway on (ADR 0082) it also says whether the deposit was received and, while it was not, offers
/// to pay it by card; without one it reads as it always did.
/// </summary>
[RequireTenant]
[Route("book/done")]
public sealed class ConfirmationController(
    IDispatcher dispatcher,
    ITenantContext tenantContext,
    ITenantLookup tenantLookup,
    IOnlineDepositCheckout deposits,
    ILocalizationService localizer) : SalonControllerBase
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
        var model = new ConfirmationViewModel(appointment, tenant?.Name ?? string.Empty, zone, PublicBookingDefaults.CancelPath(appointment.PublicToken));
        if (!deposits.IsAvailable || appointment.Deposit is null)
        {
            return View(model);
        }

        var deposit = await dispatcher.SendAsync(new DepositReceivedQuery(id), cancellationToken);
        var held = deposit is { IsHeld: true } ? deposit : null;
        return View(model with
        {
            DepositReceived = held?.Amount,
            DepositReceivedBy = held is null ? null : PaymentMethodNames.Name(localizer, held.Method, held.MethodName),
            // Nothing recorded for the visit and the visit still on: the pay step can be opened again.
            PayDepositPath = deposit is null && appointment.IsActive ? PublicBookingDefaults.PayDepositPath(id) : null,
        });
    }
}

/// <param name="CancelPath">The client's own link to cancel.</param>
public sealed record ConfirmationViewModel(PublicAppointment Appointment, string SalonName, DateTimeZone Zone, string CancelPath)
{
    /// <summary>The deposit the salon holds for this visit; null when none is held, or when no payment gateway is on.</summary>
    public Money? DepositReceived { get; init; }

    /// <summary>How it was paid, as the salon's own lists name it.</summary>
    public string? DepositReceivedBy { get; init; }

    /// <summary>Where the deposit can be paid by card now; null when there is nothing to pay online.</summary>
    public string? PayDepositPath { get; init; }
}