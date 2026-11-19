using System.Net.Mail;
using FluentValidation;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Services.Localization;

namespace Salon.Web.Features.Identity.SignUp;

/// <summary>Input rules for <see cref="SignUpCommand"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class SignUpValidator : AbstractValidator<SignUpCommand>
{
    /// <summary>Longer than anybody types by hand; keeps a pasted novel out of the hasher.</summary>
    public const int MaxPasswordLength = 128;

    public SignUpValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.SalonName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Identity.SignUp.Validation.SalonNameRequired"])
            .Must(name => name is null || name.Trim().Length <= 200)
            .WithMessage(_ => localizer.Format("Identity.SignUp.Validation.SalonNameTooLong", 200));

        RuleFor(c => c.Slug)
            .Must(slug => TenantSlugRules.IsWellFormed(TenantSlugRules.Normalize(slug)))
            .WithMessage(_ => localizer.Format("Identity.SignUp.Validation.SlugInvalid", TenantSlugRules.MinLength, TenantSlugRules.MaxLength))
            .Must(slug => !TenantSlugRules.IsReserved(TenantSlugRules.Normalize(slug)))
            .WithMessage(_ => localizer["Identity.SignUp.Validation.SlugTaken"]);

        RuleFor(c => c.Country)
            .Must(Enum.IsDefined)
            .WithMessage(_ => localizer["Identity.SignUp.Validation.CountryRequired"]);

        RuleFor(c => c.DisplayName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Identity.SignUp.Validation.NameRequired"])
            .Must(name => name is null || name.Trim().Length <= User.DisplayNameMaxLength)
            .WithMessage(_ => localizer.Format("Identity.SignUp.Validation.NameTooLong", User.DisplayNameMaxLength));

        RuleFor(c => c.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage(_ => localizer["Identity.SignUp.Validation.EmailRequired"])
            .Must(email => string.IsNullOrWhiteSpace(email) || (email.Trim().Length <= User.EmailMaxLength && IsEmail(email.Trim())))
            .WithMessage(_ => localizer["Identity.SignUp.Validation.EmailInvalid"]);

        RuleFor(c => c.Password)
            .Must(password => password is { Length: >= IUserService.MinPasswordLength })
            .WithMessage(_ => localizer.Format("Identity.SignUp.Validation.PasswordTooShort", IUserService.MinPasswordLength))
            .Must(password => password is null || password.Length <= MaxPasswordLength)
            .WithMessage(_ => localizer.Format("Identity.SignUp.Validation.PasswordTooLong", MaxPasswordLength));
    }

    /// <summary>A plain address only: "Anna &lt;anna@example.com&gt;" parses but is not what the field asks for.</summary>
    private static bool IsEmail(string candidate)
        => MailAddress.TryCreate(candidate, out var address) && string.Equals(address.Address, candidate, StringComparison.Ordinal);
}