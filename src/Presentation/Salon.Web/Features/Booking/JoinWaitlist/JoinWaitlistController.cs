using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Core.Cqrs;
using Salon.Services.Booking;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.JoinWaitlist;

/// <summary>
/// "Tell me when a time comes up" on the public booking pages (ADR 0055): <c>/book/{serviceId}/waitlist</c> takes the
/// name and number, <c>/book/waitlist/verify/{id}</c> the SMS code, <c>/book/waitlist/done</c> says it worked. Only
/// while the owner has switched it on; otherwise every address here is a 404. The number is proven with the same code
/// a booking uses before anything reaches the desk's list, and the posts are rate-limited like the booking's.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class JoinWaitlistController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("{serviceId:guid}/waitlist")]
    public async Task<IActionResult> Form(Guid serviceId, Guid? staff, CancellationToken cancellationToken)
    {
        var summary = await dispatcher.SendAsync(new WaitlistFormQuery(serviceId, staff), cancellationToken);
        return summary is null
            ? NotFound()
            : View(new JoinWaitlistViewModel(summary, new JoinWaitlistCommand(serviceId, summary.StaffMemberId, null, null, null, null, null, null, null)));
    }

    [HttpPost("{serviceId:guid}/waitlist")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicBookingDefaults.SmsCodeRateLimitPolicy)]
    public async Task<IActionResult> Form(Guid serviceId, JoinWaitlistCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var summary = await dispatcher.SendAsync(new WaitlistFormQuery(serviceId, command.StaffMemberId), cancellationToken);
        if (summary is null)
        {
            return NotFound();
        }

        // The person is whoever the salon still offers; a stale or made-up id means "anyone".
        command = command with { ServiceId = serviceId, StaffMemberId = summary.StaffMemberId };
        if (!ModelState.IsValid)
        {
            return View(new JoinWaitlistViewModel(summary, command));
        }

        PublicWaitlistStart result;
        try
        {
            result = await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(new JoinWaitlistViewModel(summary, command));
        }
        catch (PhoneVerificationRateLimitedException)
        {
            ModelState.AddModelError(nameof(JoinWaitlistCommand.Phone), localizer["Booking.Public.Validation.TooManyCodes"]);
            return View(new JoinWaitlistViewModel(summary, command));
        }

        return result.RequiresVerification
            ? Redirect($"/book/waitlist/verify/{result.VerificationId}")
            : Redirect(DonePath(serviceId));
    }

    [HttpGet("waitlist/verify/{id:guid}")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        var pending = await dispatcher.SendAsync(new PendingWaitlistQuery(id), cancellationToken);
        return pending is null
            ? NotFound()
            : View(ViewModelFor(pending, pending.IsUsable ? null : localizer["Booking.Public.Verify.Expired"], !pending.IsUsable));
    }

    [HttpPost("waitlist/verify/{id:guid}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicBookingDefaults.CodeVerifyRateLimitPolicy)]
    public async Task<IActionResult> Verify(Guid id, string? code, CancellationToken cancellationToken)
    {
        var pending = await dispatcher.SendAsync(new PendingWaitlistQuery(id), cancellationToken);
        if (pending is null)
        {
            return NotFound();
        }

        var result = await dispatcher.SendAsync(new VerifyWaitlistCodeCommand(id, code), cancellationToken);
        switch (result.Outcome)
        {
            case PublicWaitlistOutcome.Joined:
                return Redirect(DonePath(pending.ServiceId));
            case PublicWaitlistOutcome.WrongCode:
                ModelState.AddModelError("Code", localizer.Plural("Booking.Public.Verify.Wrong", result.AttemptsLeft));
                return View(ViewModelFor(pending, null, false));
            case PublicWaitlistOutcome.Expired:
                return View(ViewModelFor(pending, localizer["Booking.Public.Verify.Expired"], true));
            case PublicWaitlistOutcome.TooManyAttempts:
                return View(ViewModelFor(pending, localizer["Booking.Public.Verify.TooMany"], true));
            case PublicWaitlistOutcome.NoLongerPossible:
                return View(ViewModelFor(pending, localizer["Booking.Waitlist.NoLongerPossible"], true));
            case PublicWaitlistOutcome.NotFound:
                return NotFound();
            default:
                throw new InvalidOperationException($"Unexpected waitlist outcome {result.Outcome}.");
        }
    }

    [HttpGet("waitlist/done")]
    public async Task<IActionResult> Done(Guid? service, CancellationToken cancellationToken)
    {
        // The page says nothing about anybody: it only needs the form to be on and a service to go back to.
        var summary = service is { } serviceId ? await dispatcher.SendAsync(new WaitlistFormQuery(serviceId, null), cancellationToken) : null;
        return summary is null ? NotFound() : View(summary);
    }

    public static string FormPath(Guid serviceId) => $"/book/{serviceId}/waitlist";

    private static string DonePath(Guid serviceId) => $"/book/waitlist/done?service={serviceId}";

    private static VerifyWaitlistViewModel ViewModelFor(PendingVerification pending, string? message, bool startAgain)
        => new(pending.Id, PhoneNumber.Mask(pending.PhoneE164), pending.ServiceId, message, startAgain);
}

public sealed record JoinWaitlistViewModel(WaitlistFormSummary Summary, JoinWaitlistCommand Form);

/// <param name="Message">Why the code can no longer be used; null while it can.</param>
/// <param name="StartAgain">True when the row is dead: the page offers the form again instead of the code field.</param>
public sealed record VerifyWaitlistViewModel(Guid Id, string MaskedPhone, Guid ServiceId, string? Message, bool StartAgain);