using System.Net.Mime;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Api.Contracts.Booking;
using Salon.Core.Cqrs;
using Salon.Services.Appointments;
using Salon.Services.Booking;
using Salon.Services.Clients;
using Salon.Web.Framework.Api;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Booking.Code;

namespace Salon.Web.Features.Api.Booking.StartBooking;

/// <summary>
/// <c>POST /api/v1/booking/start</c> (ADR 0097): the details form's POST, as JSON. The same check of the visit first
/// (an unknown service or a person who is not bookable for it is a 404), then the same command: a code texted to the
/// client, or with codes off the visit booked at once. Nothing is remembered on the device here; the app keeps its own.
/// </summary>
[Route(BookingApi.Root)]
public sealed class StartApiController(IDispatcher dispatcher, IValidator<StartBookingRequest> validator) : BookingApiControllerBase
{
    [HttpPost("start")]
    [Consumes(MediaTypeNames.Application.Json)]
    [EnableRateLimiting(PublicBookingDefaults.SmsCodeRateLimitPolicy)]
    public async Task<ActionResult<StartBookingResponse>> Start([FromBody] StartBookingRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || !ModelState.IsValid)
        {
            return Unreadable(ModelState);
        }

        var check = await validator.ValidateAsync(request, cancellationToken);
        if (!check.IsValid)
        {
            return Invalid(check.Errors);
        }

        // The validator has refused a start that is blank or does not read.
        var start = ApiFormat.ParseInstant(request.Start)!.Value;
        if (await dispatcher.SendAsync(StartBookingMapper.ToSummaryQuery(request, start), cancellationToken) is null)
        {
            return Missing();
        }

        PublicBookingStart result;
        try
        {
            result = await dispatcher.SendAsync(StartBookingMapper.ToCommand(request), cancellationToken);
        }
        catch (ValidationException ex)
        {
            return Invalid(ex.Errors);
        }
        catch (PhoneVerificationRateLimitedException)
        {
            return TooManyCodes();
        }
        catch (SlotNoLongerAvailableException)
        {
            return Taken();
        }

        // Where the code went, hidden as the code page hides it; read back from the verification, not from the body.
        var pending = result.VerificationId is { } id ? await dispatcher.SendAsync(new PendingVerificationQuery(id), cancellationToken) : null;
        return Ok(new StartBookingResponse(result.VerificationId, result.AppointmentId, pending is null ? null : PhoneNumber.Mask(pending.PhoneE164)));
    }
}