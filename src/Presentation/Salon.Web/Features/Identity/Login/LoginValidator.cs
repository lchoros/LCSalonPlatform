using FluentValidation;

using Salon.Services.Localization;

namespace Salon.Web.Features.Identity.Login;

/// <summary>Both fields are required; nothing else is checked here so the form never hints at what a valid account looks like.</summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage(_ => localizer["Identity.Login.Validation.EmailRequired"]);

        RuleFor(c => c.Password)
            .Must(password => !string.IsNullOrEmpty(password))
            .WithMessage(_ => localizer["Identity.Login.Validation.PasswordRequired"]);
    }
}