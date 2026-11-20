using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.ClientPortal;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.ClientPortal.RequestCode;

/// <summary>
/// <c>/my-bookings/sign-in</c> (ADR 0083): the client types the mobile number they book with. A number that can be
/// read always leads to the code page, known to the salon or not, so the page tells nobody who is a client here. The
/// POST is anti-forgery protected and shares the booking flow's limits: so many posts per address
/// (<see cref="PublicBookingDefaults.SmsCodeRateLimitPolicy"/>) and so many codes per number an hour.
/// </summary>
[ClientPortalGate]
public sealed class RequestCodeController(
    IDispatcher dispatcher,
    ILocalizationService localizer,
    ITenantContext tenantContext,
    IClientPortalSession session) : SalonControllerBase
{
    [HttpGet(ClientPortalDefaults.SignInPath)]
    public IActionResult SignIn()
    {
        // Already in: straight to the visits, which decide whether the session still stands.
        if (tenantContext.TenantId is { } tenantId && session.Read(Request, tenantId) is not null)
        {
            return Redirect(ClientPortalDefaults.VisitsPath);
        }

        return View(new RequestCodeViewModel(null, TakeFlash(), PublicBookingDefaults.BookPath));
    }

    [HttpPost(ClientPortalDefaults.SignInPath)]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicBookingDefaults.SmsCodeRateLimitPolicy)]
    public async Task<IActionResult> SignIn(RequestCodeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            var verificationId = await dispatcher.SendAsync(command, cancellationToken);
            return Redirect(ClientPortalDefaults.CodePath(verificationId));
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
        }
        catch (PhoneVerificationRateLimitedException)
        {
            ModelState.AddModelError(nameof(RequestCodeCommand.Phone), localizer["Booking.Public.Validation.TooManyCodes"]);
        }

        return View(new RequestCodeViewModel(command.Phone, null, PublicBookingDefaults.BookPath));
    }
}

/// <param name="Phone">What was typed, for the form to show again.</param>
/// <param name="Flash">A one-line confirmation left by the previous request (signed out), or null.</param>
public sealed record RequestCodeViewModel(string? Phone, string? Flash, string BookPath);