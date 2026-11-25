using FluentValidation;
using Salon.Api.Contracts.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Api;
using Salon.Web.Framework.Booking;

namespace Salon.Web.Features.Api.Booking.CheckAvailability;

/// <summary>
/// What the time screen sends: the services and the answers so far (an answer may still be owed), and days and a time
/// in the forms the API writes them. A day or a time that reads is used as the page uses it; one that does not is a fault.
/// </summary>
public sealed class AvailabilityRequestValidator : AbstractValidator<AvailabilityRequest>
{
    public AvailabilityRequestValidator(IVisitBasket basket, ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(localizer);

        this.AddVisitPartsRules(r => r.Parts, basket, localizer, answerRequired: false);

        RuleFor(r => r.From)
            .Must(from => ApiFormat.TryParseDate(from, out _))
            .WithMessage(_ => localizer["Booking.Api.Validation.DateInvalid"]);

        RuleFor(r => r.Date)
            .Must(date => ApiFormat.TryParseDate(date, out _))
            .WithMessage(_ => localizer["Booking.Api.Validation.DateInvalid"]);

        RuleFor(r => r.Start)
            .Must(start => ApiFormat.TryParseInstant(start, out _))
            .WithMessage(_ => localizer["Booking.Public.Validation.StartInvalid"]);
    }
}