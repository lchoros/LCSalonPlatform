using System.Net.Mime;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Services.Booking;
using Salon.Web.Framework.Api;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Booking.Code;

namespace Salon.Web.Features.Api.Booking.VerifyCode;

/// <summary>
/// <c>POST /api/v1/booking/verify/{verificationId}</c> (ADR 0097): the code page's POST, as JSON. The id is the only
/// state, and it names a verification of this salon or nothing (a 404). Anything but six digits is refused before an
/// attempt is spent; otherwise the answer says what happened, as the page does: booked, a wrong code with the attempts
/// left, or a reason to go back (expired, too many attempts, the time taken meanwhile).
/// </summary>
[Route(BookingApi.Root)]
public sealed class VerifyApiController(IDispatcher dispatcher) : BookingApiControllerBase
{
    [HttpPost("verify/{verificationId:guid}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [EnableRateLimiting(PublicBookingDefaults.CodeVerifyRateLimitPolicy)]
    public async Task<ActionResult<VerifyResponse>> Verify(Guid verificationId, [FromBody] VerifyRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || !ModelState.IsValid)
        {
            return Unreadable(ModelState);
        }

        if (await dispatcher.SendAsync(new PendingVerificationQuery(verificationId), cancellationToken) is null)
        {
            return Missing();
        }

        PublicBookingVerification result;
        try
        {
            result = await dispatcher.SendAsync(new VerifyCodeCommand(verificationId, request.Code), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return Invalid(ex.Errors);
        }

        return VerifyResponseMapper.Outcome(result.Outcome) is { } outcome
            ? Ok(new VerifyResponse(outcome, result.AppointmentId, result.AttemptsLeft))
            : Missing();
    }
}