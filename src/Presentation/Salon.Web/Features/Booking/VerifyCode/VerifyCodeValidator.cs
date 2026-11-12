using FluentValidation;

using Salon.Core.Booking;
using Salon.Services.Localization;

namespace Salon.Web.Features.Booking.VerifyCode;

public sealed class VerifyCodeValidator : AbstractValidator<VerifyCodeCommand>
{
    public VerifyCodeValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Code)
            .Must(code => PhoneVerificationRules.TryNormaliseCode(code, out _))
            .WithMessage(_ => localizer["Booking.Public.Validation.CodeInvalid"]);
    }
}
