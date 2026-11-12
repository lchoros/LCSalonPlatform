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

namespace Salon.Web.Features.Booking.VerifyCode;

/// <summary>
/// Step 4 at <c>/book/verify/{id}</c> (ADR 0014): the six-digit code. The id in the URL is the only state; the row it
/// names belongs to this salon or does not exist. A right code books and lands on the confirmation; a wrong one says how
/// many attempts remain; a dead or expired row offers to start again. The POST is anti-forgery protected and rate-limited per address.
/// </summary>
[RequireTenant]
[Route("book/verify")]
public sealed class VerifyCodeController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Verify(Guid id, CancellationToken cancellationToken)
    {
        var pending = await dispatcher.SendAsync(new PendingVerificationQuery(id), cancellationToken);
        if (pending is null)
        {
            return NotFound();
        }

        if (pending.AppointmentId is { } appointmentId)
        {
            return Redirect($"/book/done/{appointmentId}");
        }

        return View(ViewModelFor(pending, pending.IsUsable ? VerifyState.Open : VerifyState.StartAgain));
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicBookingDefaults.CodeVerifyRateLimitPolicy)]
    public async Task<IActionResult> Verify(Guid id, VerifyCodeForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var pending = await dispatcher.SendAsync(new PendingVerificationQuery(id), cancellationToken);
        if (pending is null)
        {
            return NotFound();
        }

        PublicBookingVerification result;
        try
        {
            result = await dispatcher.SendAsync(new VerifyCodeCommand(id, form.Code), cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(ViewModelFor(pending, VerifyState.Open));
        }

        switch (result.Outcome)
        {
            case PublicBookingVerificationOutcome.Booked:
                return Redirect($"/book/done/{result.AppointmentId}");
            case PublicBookingVerificationOutcome.WrongCode:
                ModelState.AddModelError(nameof(VerifyCodeForm.Code), localizer.Format("Booking.Public.Verify.Wrong", result.AttemptsLeft));
                return View(ViewModelFor(pending, VerifyState.Open));
            case PublicBookingVerificationOutcome.Expired:
                return View(ViewModelFor(pending, VerifyState.StartAgain, localizer["Booking.Public.Verify.Expired"]));
            case PublicBookingVerificationOutcome.TooManyAttempts:
                return View(ViewModelFor(pending, VerifyState.StartAgain, localizer["Booking.Public.Verify.TooMany"]));
            case PublicBookingVerificationOutcome.SlotTaken:
                return View(ViewModelFor(pending, VerifyState.PickAnother, localizer["Booking.Public.SlotTaken"]));
            case PublicBookingVerificationOutcome.NotFound:
                return NotFound();
            default:
                throw new InvalidOperationException($"Unexpected verification outcome {result.Outcome}.");
        }
    }

    private VerifyCodeViewModel ViewModelFor(PendingVerification pending, VerifyState state, string? message = null)
        => new(pending.Id, PhoneNumber.Mask(pending.PhoneE164), pending.ServiceId, state, message ?? (state == VerifyState.StartAgain ? localizer["Booking.Public.Verify.Expired"] : null));
}

/// <summary>The code as posted. Bound by MVC; the rule lives in <see cref="VerifyCodeValidator"/>.</summary>
public sealed record VerifyCodeForm(string? Code);

public enum VerifyState
{
    /// <summary>The row can still take a code.</summary>
    Open,

    /// <summary>Dead or expired: back to the details form.</summary>
    StartAgain,

    /// <summary>Verified, but the slot went: back to the time picker.</summary>
    PickAnother,
}

/// <param name="MaskedPhone">Where the code went, with the middle hidden.</param>
/// <param name="Message">A localized explanation shown when <paramref name="State"/> is not <see cref="VerifyState.Open"/>.</param>
public sealed record VerifyCodeViewModel(Guid VerificationId, string MaskedPhone, Guid ServiceId, VerifyState State, string? Message);