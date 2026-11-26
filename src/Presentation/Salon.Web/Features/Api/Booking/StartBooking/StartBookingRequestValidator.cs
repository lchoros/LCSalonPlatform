using FluentValidation;
using Salon.Api.Contracts.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Api;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Features.Api.Booking.StartBooking;

/// <summary>
/// The shape of the visit posted with the details: its services, an answer to "who with" for each, and the time. The
/// client's own details are checked by the details form's rules (<c>StartBookingValidator</c>, <c>StartBookingPhoneValidator</c>)
/// when the command is sent, so a name or a number is refused in the same words on the page and in the app.
/// </summary>
public sealed class StartBookingRequestValidator : AbstractValidator<StartBookingRequest>
{
    public StartBookingRequestValidator(IVisitBasket basket, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(localizer);

        this.AddVisitPartsRules(r => r.Parts, basket, localizer, answerRequired: true);

        RuleFor(r => r.Start)
            .Must(start => !string.IsNullOrWhiteSpace(start) && ApiFormat.TryParseInstant(start, out _))
            .WithMessage(_ => localizer["Booking.Public.Validation.StartInvalid"]);
    }
}