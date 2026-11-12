using FluentValidation;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Cqrs;
using Salon.Services.Appointments;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>
/// Step 3 at <c>/book/{serviceId}/details?start=…&amp;staff=…</c> (ADR 0014): the chosen slot and the client's details.
/// A valid POST sends the SMS code and lands on the verify page (or, with verification off, on the confirmation); an
/// invalid one re-renders with messages next to the fields. The POST is anti-forgery protected and rate-limited per
/// address because each one may send an SMS.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class EnterDetailsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("{serviceId:guid}/details")]
    public async Task<IActionResult> Details(Guid serviceId, string? start, Guid? staff, CancellationToken cancellationToken)
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
        return View(new EnterDetailsViewModel(summary, form));
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

/// <param name="SlotTaken">True when the immediate booking path found the slot gone; the page offers the time picker again.</param>
public sealed record EnterDetailsViewModel(BookingSummary Summary, StartBookingCommand Form, bool SlotTaken = false);