using System.Net.Mime;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Salon.Api.Contracts.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Framework.Api;

/// <summary>
/// Base of the public booking API, <c>/api/v1/booking</c> on the salon's host (ADR 0097, docs/mobile/BOOKING-API.md):
/// anonymous like the booking pages, a 404 on the bare platform domain like them, JSON in and out, and every refusal an
/// <see cref="ApiProblem"/> with a sentence in the salon's language.
/// </summary>
/// <remarks>
/// No anti-forgery token: nothing here signs anybody in or reads a cookie, everything it does the public pages let
/// anyone do, and a POST takes <c>application/json</c> only, which a page of another site cannot send without the
/// browser asking first. The per-address rate limits of the pages apply to <c>start</c> and <c>verify</c> all the same.
/// </remarks>
[RequireTenant]
[IgnoreAntiforgeryToken]
[Produces(MediaTypeNames.Application.Json)]
public abstract class BookingApiControllerBase : SalonControllerBase
{
    private ILocalizationService Localizer => HttpContext.RequestServices.GetRequiredService<ILocalizationService>();

    /// <summary>400 with the messages by field, as the validators named them ("FirstName", "Parts[1].Staff").</summary>
    protected ObjectResult Invalid(IEnumerable<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var fields = failures
            .GroupBy(f => f.PropertyName, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        return Refusal(StatusCodes.Status400BadRequest, ApiProblemCodes.Validation, Localizer["Booking.Api.Invalid"], fields);
    }

    /// <summary>400 for a body that did not even read as the shape asked for: the places that failed, without the parser's own English.</summary>
    protected ObjectResult Unreadable(ModelStateDictionary modelState)
    {
        ArgumentNullException.ThrowIfNull(modelState);
        var message = Localizer["Booking.Api.Validation.BodyInvalid"];
        var fields = modelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(e => e.Key.Length == 0 ? "body" : e.Key, _ => new[] { message }, StringComparer.Ordinal);
        return Refusal(StatusCodes.Status400BadRequest, ApiProblemCodes.Validation, message, fields);
    }

    /// <summary>404: nothing of this salon goes by the id, or the service or person asked for is not bookable here.</summary>
    protected ObjectResult Missing() => Refusal(StatusCodes.Status404NotFound, ApiProblemCodes.NotFound, Localizer["Booking.Api.NotFound"]);

    /// <summary>409: the time went while the client was typing.</summary>
    protected ObjectResult Taken() => Refusal(StatusCodes.Status409Conflict, ApiProblemCodes.SlotTaken, Localizer["Booking.Public.SlotTaken"]);

    /// <summary>429: the number has had all the codes it may have this hour.</summary>
    protected ObjectResult TooManyCodes() => Refusal(StatusCodes.Status429TooManyRequests, ApiProblemCodes.TooManyCodes, Localizer["Booking.Public.Validation.TooManyCodes"]);

    private static ObjectResult Refusal(int status, string code, string message, IReadOnlyDictionary<string, string[]>? fields = null)
        => new(new ApiProblem(code, message, fields)) { StatusCode = status };
}