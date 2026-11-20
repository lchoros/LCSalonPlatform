using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.ClientPortal;
using Salon.Services.Clients;
using Salon.Services.Localization;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.ClientPortal;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.ClientPortal.EnterCode;

/// <summary>
/// <c>/my-bookings/code/{id}</c> (ADR 0083): the six digits from the text message. The id in the address is the
/// verification row's and belongs to this salon or does not exist. The right code starts the client's session and
/// leads to their visits. Every other answer is one sentence, the same whether the code was wrong, too old, tried
/// too often, already used, or was never sent because the salon does not know the number. The POST is anti-forgery
/// protected and shares the booking flow's limit on attempts per address
/// (<see cref="PublicBookingDefaults.CodeVerifyRateLimitPolicy"/>).
/// </summary>
[ClientPortalGate]
public sealed class EnterCodeController(
    IDispatcher dispatcher,
    ILocalizationService localizer,
    ITenantContext tenantContext,
    IClientPortalSession session) : SalonControllerBase
{
    private const string CodeField = "Code";

    [HttpGet(ClientPortalDefaults.CodePathPrefix + "/{id:guid}")]
    public async Task<IActionResult> Code(Guid id, CancellationToken cancellationToken)
    {
        var pending = await dispatcher.SendAsync(new PendingCodeQuery(id), cancellationToken);
        return pending is null ? NotFound() : View(ViewModelFor(pending));
    }

    [HttpPost(ClientPortalDefaults.CodePathPrefix + "/{id:guid}")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(PublicBookingDefaults.CodeVerifyRateLimitPolicy)]
    public async Task<IActionResult> Code(Guid id, string? code, CancellationToken cancellationToken)
    {
        var pending = await dispatcher.SendAsync(new PendingCodeQuery(id), cancellationToken);
        if (pending is null)
        {
            return NotFound();
        }

        ClientPortalSignIn result;
        try
        {
            result = await dispatcher.SendAsync(new EnterCodeCommand(id, code), cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(ViewModelFor(pending));
        }

        if (result is { Outcome: ClientPortalSignInOutcome.SignedIn, ClientId: { } clientId } && tenantContext.TenantId is { } tenantId)
        {
            session.Start(Response, tenantId, clientId);
            return Redirect(ClientPortalDefaults.VisitsPath);
        }

        if (result.Outcome == ClientPortalSignInOutcome.NotFound)
        {
            return NotFound();
        }

        ModelState.AddModelError(CodeField, localizer["ClientPortal.Code.Refused"]);
        return View(ViewModelFor(pending));
    }

    private static EnterCodeViewModel ViewModelFor(ClientPortalPendingCode pending)
        => new(PhoneNumber.Mask(pending.PhoneE164), ClientPortalDefaults.SignInPath);
}

/// <param name="MaskedPhone">The number that was typed, with the middle hidden.</param>
/// <param name="SignInPath">Where to ask for a new code, or to type another number.</param>
public sealed record EnterCodeViewModel(string MaskedPhone, string SignInPath);