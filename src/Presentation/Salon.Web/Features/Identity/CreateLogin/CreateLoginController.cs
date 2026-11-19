using System.Net.Mail;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.CreateLogin;

/// <summary>
/// Adds one login to the current salon (ADR 0076). The owner types a starting password and hands it over in person;
/// there are no email invitations in phase 1. The same email as a turned-off login is refused here — that login is
/// turned back on instead, from its own page.
/// </summary>
public sealed record CreateLoginCommand(string DisplayName, string Email, Role Role, string Password) : ICommand<Guid>
{
    public static CreateLoginCommand Empty { get; } = new(string.Empty, string.Empty, Role.Receptionist, string.Empty);
}

/// <summary>Input rules for <see cref="CreateLoginCommand"/>. Messages come from resource keys, resolved in the request culture.</summary>
public sealed class CreateLoginValidator : AbstractValidator<CreateLoginCommand>
{
    public const int MaxPasswordLength = 128;

    public CreateLoginValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.DisplayName)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage(_ => localizer["Identity.Team.Validation.NameRequired"])
            .Must(name => name is null || name.Trim().Length <= User.DisplayNameMaxLength)
            .WithMessage(_ => localizer.Format("Identity.Team.Validation.NameTooLong", User.DisplayNameMaxLength));

        RuleFor(c => c.Email)
            .Must(email => !string.IsNullOrWhiteSpace(email))
            .WithMessage(_ => localizer["Identity.Team.Validation.EmailRequired"])
            .Must(email => string.IsNullOrWhiteSpace(email) || (email.Trim().Length <= User.EmailMaxLength && IsEmail(email.Trim())))
            .WithMessage(_ => localizer["Identity.Team.Validation.EmailInvalid"]);

        RuleFor(c => c.Role)
            .Must(Enum.IsDefined)
            .WithMessage(_ => localizer["Identity.Team.Validation.RoleRequired"]);

        RuleFor(c => c.Password)
            .Must(password => password is { Length: >= IUserService.MinPasswordLength })
            .WithMessage(_ => localizer.Format("Identity.Team.Validation.PasswordTooShort", IUserService.MinPasswordLength))
            .Must(password => password is null || password.Length <= MaxPasswordLength)
            .WithMessage(_ => localizer.Format("Identity.Team.Validation.PasswordTooLong", MaxPasswordLength));
    }

    /// <summary>A plain address only: "Anna &lt;anna@example.com&gt;" parses but is not what the field asks for.</summary>
    private static bool IsEmail(string candidate)
        => MailAddress.TryCreate(candidate, out var address) && string.Equals(address.Address, candidate, StringComparison.Ordinal);
}

public sealed class CreateLoginHandler(IUserService users, ILocalizationService localizer) : IRequestHandler<CreateLoginCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateLoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return (await users.CreateAsync(request.Email, request.DisplayName.Trim(), request.Password, request.Role, cancellationToken)).Id;
        }
        catch (InvalidOperationException)
        {
            // The email is this salon's already, on or off; the service's message is for logs, this one for the form.
            throw new ValidationException([new ValidationFailure(nameof(CreateLoginCommand.Email), localizer["Identity.Team.Validation.EmailTaken"])]);
        }
    }
}

/// <summary>
/// <c>/admin/settings/team/new</c>, owner only (ADR 0076): name, email, role and a starting password for one new
/// login. Plain post-redirect-get back to the team list.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/team")]
public sealed class CreateLoginController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    private const string ListPath = "/admin/settings/team";

    [HttpGet("new")]
    public IActionResult New() => View(CreateLoginCommand.Empty);

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(CreateLoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!ModelState.IsValid)
        {
            return View(command);
        }

        try
        {
            await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(command);
        }

        SetFlash(localizer.Format("Identity.Team.Created", command.DisplayName.Trim()));
        return Redirect(ListPath);
    }
}