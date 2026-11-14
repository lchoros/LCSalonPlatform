using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Appointments;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// Step 3 of the public flow (ADR 0014): name, phone, optional email, consent. A device that has booked here before gets
/// the form prefilled from its own protected cookie (<see cref="IReturningClientCookie"/>); nothing is looked up before
/// the phone is verified, and "Not you?" clears it.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class EnterDetailsController(
    IDispatcher dispatcher,
    ILocalizationService localizer,
    ITenantContext tenantContext,
    IReturningClientCookie returning) : SalonControllerBase
{
    [HttpGet("{serviceId:guid}/details")]
    public async Task<IActionResult> Details(Guid serviceId, string? start, Guid? staff, bool forget, CancellationToken cancellationToken)
    {
        var instant = ParseStart(start);
        if (instant is null)
        {
            return Redirect($"/book/{serviceId}");
        }

        var summary = await dispatcher.SendAsync(new BookingSummaryQuery(serviceId, staff, instant.Value), cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        var form = new StartBookingCommand(serviceId, InstantPattern.ExtendedIso.Format(instant.Value), staff, null, null, null, null, false);
        if (forget)
        {
            returning.Forget(Response);
            return View(new EnterDetailsViewModel(summary, form));
        }

        var remembered = tenantContext.TenantId is { } tenantId ? returning.Read(Request, tenantId) : null;
        if (remembered is null)
        {
            return View(new EnterDetailsViewModel(summary, form));
        }

        form = form with { FirstName = remembered.FirstName, LastName = remembered.LastName, Phone = remembered.Phone, Email = remembered.Email };
        return View(new EnterDetailsViewModel(summary, form, PrefilledFor: remembered.FirstName));
    }

    [HttpPost("{serviceId:guid}/details")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicBookingDefaults.SmsCodeRateLimitPolicy)]
    public async Task<IActionResult> Details(Guid serviceId, StartBookingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { ServiceId = serviceId };

        var instant = ParseStart(command.Start);
        if (instant is null)
        {
            return Redirect($"/book/{serviceId}");
        }

        var summary = await dispatcher.SendAsync(new BookingSummaryQuery(serviceId, command.StaffMemberId, instant.Value), cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(new EnterDetailsViewModel(summary, command));
        }

        PublicBookingStart result;
        try
        {
            result = await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(new EnterDetailsViewModel(summary, command));
        }
        catch (PhoneVerificationRateLimitedException)
        {
            ModelState.AddModelError(nameof(StartBookingCommand.Phone), localizer["Booking.Public.Validation.TooManyCodes"]);
            return View(new EnterDetailsViewModel(summary, command));
        }
        catch (SlotNoLongerAvailableException)
        {
            return View(new EnterDetailsViewModel(summary, command, SlotTaken: true));
        }

        // Valid details: remember them on this device only (what the client just typed, nothing from the salon's records).
        if (tenantContext.TenantId is { } tenantId)
        {
            returning.Write(Response, new ReturningClient(tenantId, command.FirstName ?? string.Empty, command.LastName ?? string.Empty, command.Phone ?? string.Empty, command.Email));
        }

        return result.RequiresVerification
            ? Redirect($"/book/verify/{result.VerificationId}")
            : Redirect($"/book/done/{result.AppointmentId}");
    }

    private static Instant? ParseStart(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var parsed = InstantPattern.ExtendedIso.Parse(start.Trim());
        return parsed.Success ? parsed.Value : null;
    }
}

/// <param name="PrefilledFor">The remembered first name when the form came from the device's cookie; null otherwise.</param>
public sealed record EnterDetailsViewModel(BookingSummary Summary, StartBookingCommand Form, bool SlotTaken = false, string? PrefilledFor = null);