using Microsoft.AspNetCore.Mvc;

using NodaTime;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.CancelBooking;

/// <summary>
/// <c>/book/cancel/{token}</c> (ADR 0014). GET shows the appointment and a confirm button; POST cancels it as the client.
/// A token this salon does not know, one already used, or an appointment that has started gets a localized message on
/// the same page (200, not 404: the client followed a link from their own SMS and deserves an explanation).
/// </summary>
[RequireTenant]
[Route("book/cancel")]
public sealed class CancelBookingController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup, IClock clock) : SalonControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Cancel(string token, CancellationToken cancellationToken)
    {
        var appointment = await dispatcher.SendAsync(new CancelBookingQuery(token), cancellationToken);
        var state = appointment switch
        {
            null => CancelState.Invalid,
            { IsActive: false } => CancelState.Already,
            _ when appointment.Start <= clock.GetCurrentInstant() => CancelState.TooLate,
            _ => CancelState.Confirm,
        };
        return View(await ViewModelAsync(appointment, state, cancellationToken));
    }

    [HttpPost("{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelConfirmed(string token, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new CancelBookingCommand(token), cancellationToken);
        var state = result.Outcome switch
        {
            PublicCancellationOutcome.Cancelled => CancelState.Cancelled,
            PublicCancellationOutcome.AlreadyCancelled => CancelState.Already,
            PublicCancellationOutcome.TooLate => CancelState.TooLate,
            _ => CancelState.Invalid,
        };
        return View("Cancel", await ViewModelAsync(result.Appointment, state, cancellationToken));
    }

    private async Task<CancelBookingViewModel> ViewModelAsync(PublicAppointment? appointment, CancelState state, CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404; instants are shown in the salon's zone (rule 5).
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var zone = (tenant is null ? null : DateTimeZoneProviders.Tzdb.GetZoneOrNull(tenant.TimeZoneId)) ?? DateTimeZone.Utc;
        return new CancelBookingViewModel(appointment, state, tenant?.Name ?? string.Empty, zone);
    }
}

public enum CancelState
{
    Confirm,
    Cancelled,
    Already,
    TooLate,
    Invalid,
}

public sealed record CancelBookingViewModel(PublicAppointment? Appointment, CancelState State, string SalonName, DateTimeZone Zone);
