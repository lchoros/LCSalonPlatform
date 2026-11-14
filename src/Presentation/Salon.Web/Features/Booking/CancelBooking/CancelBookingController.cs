using Microsoft.AspNetCore.Mvc;
using NodaTime;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Payments;
using Salon.Core.Settings;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Settings;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
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
public sealed class CancelBookingController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup, ISettingService settings, IClock clock) : SalonControllerBase
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

    /// <summary>
    /// <c>/v/{token}</c> (ADR 0031): the short address messages carry. It only forwards to the page above, which also
    /// explains a link that is unknown or used; the token is escaped, so the target is always a path on this site.
    /// </summary>
    [HttpGet(PublicBookingDefaults.ShortLinkPrefix + "/{token}")]
    public IActionResult ShortLink(string token) => LocalRedirect(PublicBookingDefaults.CancelPath(token));

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
        // Inside the notice period of the salon's policy (ADR 0022) a change is no longer offered and a cancellation may
        // carry a fee: the client is told before they confirm, and again once it is done.
        var insideNotice = false;
        Money? lateFee = null;
        if (appointment is not null && state is CancelState.Confirm or CancelState.Cancelled)
        {
            var policy = await settings.LoadAsync<CancellationPolicySettings>(cancellationToken);
            insideNotice = CancellationPolicy.IsInsideNotice(appointment.Start, clock.GetCurrentInstant(), policy.FreeChangeHours);
            var amount = insideNotice ? CancellationPolicy.FeeAmount(appointment.Price.Amount, policy.LateCancellationFeePercent) : 0m;
            lateFee = amount > 0m ? appointment.Price with { Amount = amount } : null;
        }

        return new CancelBookingViewModel(appointment, state, tenant?.Name ?? string.Empty, zone, insideNotice, lateFee);

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

/// <param name="InsideNotice">True when the free-change period of the salon's policy is over (ADR 0022).</param>
/// <param name="LateFee">The late-cancellation fee the salon may charge; null when the policy sets none or the cancellation is in time.</param>
public sealed record CancelBookingViewModel(PublicAppointment? Appointment, CancelState State, string SalonName, DateTimeZone Zone, bool InsideNotice = false, Money? LateFee = null);