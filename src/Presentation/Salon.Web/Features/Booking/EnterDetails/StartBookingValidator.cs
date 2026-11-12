using FluentValidation;

using NodaTime.Text;

using Salon.Core.Clients;
using Salon.Services.Clients;
using Salon.Services.Localization;

namespace Salon.Web.Features.Booking.EnterDetails;

/// <summary>Field rules that need no database or tenant: names, email shape, the slot instant. Messages come from resource keys.</summary>
public sealed class StartBookingValidator : AbstractValidator<StartBookingCommand>
{
    public StartBookingValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Start)
            .Must(start => !string.IsNullOrWhiteSpace(start) && InstantPattern.ExtendedIso.Parse(start.Trim()).Success)
            .WithMessage(_ => localizer["Booking.Public.Validation.StartInvalid"]);

        RuleFor(c => c.FirstName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Booking.Public.Validation.FirstNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Booking.Public.Validation.NameTooLong", Client.NameMaxLength));

        RuleFor(c => c.LastName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Booking.Public.Validation.LastNameRequired"])
            .Must(name => name is null || name.Trim().Length <= Client.NameMaxLength)
            .WithMessage(_ => localizer.Format("Booking.Public.Validation.NameTooLong", Client.NameMaxLength));

        RuleFor(c => c.Phone)
            .Must(phone => !string.IsNullOrWhiteSpace(phone))
            .WithMessage(_ => localizer["Booking.Public.Validation.PhoneRequired"]);

        RuleFor(c => c.Email)
            .Must(email => EmailAddress.TryNormalise(email, out _))
            .WithMessage(_ => localizer["Booking.Public.Validation.EmailInvalid"]);
    }
}
