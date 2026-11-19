using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.EditLogin;

/// <summary>
/// Changes one login (ADR 0076): the role, a new password (left empty, the password stays), and whether the login
/// is on. Every applied change rotates the security stamp, which signs that person out everywhere. The one rule
/// that cannot be broken: the salon always keeps at least one active owner login, so the last one can be neither
/// demoted nor turned off — nobody can lock a salon out of itself.
/// </summary>
/// <param name="Password">A new password, or empty to keep the one there is.</param>
public sealed record EditLoginCommand(Guid UserId, Role Role, string? Password, bool IsActive) : ICommand<EditLoginResult?>;

/// <param name="DisplayName">Whose login was changed, for the confirmation.</param>
public sealed record EditLoginResult(string DisplayName);

/// <summary>Input rules for <see cref="EditLoginCommand"/>; the last-owner rule needs the salon and lives in the handler.</summary>
public sealed class EditLoginValidator : AbstractValidator<EditLoginCommand>
{
    /// <summary>The same cap the create form sets; a slice never references another slice, so each names it.</summary>
    public const int MaxPasswordLength = 128;

    public EditLoginValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Role)
            .Must(Enum.IsDefined)
            .WithMessage(_ => localizer["Identity.Team.Validation.RoleRequired"]);

        RuleFor(c => c.Password)
            .Must(password => string.IsNullOrEmpty(password) || password.Length >= IUserService.MinPasswordLength)
            .WithMessage(_ => localizer.Format("Identity.Team.Validation.PasswordTooShort", IUserService.MinPasswordLength))
            .Must(password => password is null || password.Length <= MaxPasswordLength)
            .WithMessage(_ => localizer.Format("Identity.Team.Validation.PasswordTooLong", MaxPasswordLength));
    }
}

public sealed class EditLoginHandler(IUserService users, ILocalizationService localizer) : IRequestHandler<EditLoginCommand, EditLoginResult?>
{

    public async Task<EditLoginResult?> HandleAsync(EditLoginCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.FindByIdAsync(request.UserId, cancellationToken);
        if (user is null)
        {
            // Another salon's id looks the same as a missing one (tenant filter).
            return null;
        }

        // The salon always keeps at least one active owner login (ADR 0076): count the others, not the edited one.
        var wasActiveOwner = user is { IsActive: true, Role: Role.Owner };
        var staysActiveOwner = request is { IsActive: true, Role: Role.Owner };
        if (wasActiveOwner && !staysActiveOwner)
        {
            var otherActiveOwners = (await users.ListAsync(cancellationToken)).Count(u => u.Id != user.Id && u.IsActive && u.Role == Role.Owner);
            if (otherActiveOwners == 0)
            {
                throw new ValidationException([new ValidationFailure(nameof(EditLoginCommand.Role), localizer["Identity.Team.Validation.LastOwner"])]);
            }
        }

        if (user.Role != request.Role)
        {
            await users.SetRoleAsync(user.Id, request.Role, cancellationToken);
        }

        if (request.Password is { Length: > 0 } password)
        {
            await users.SetPasswordAsync(user.Id, password, cancellationToken);
        }

        if (user.IsActive != request.IsActive)
        {
            await (request.IsActive ? users.ReactivateAsync(user.Id, cancellationToken) : users.DeactivateAsync(user.Id, cancellationToken));
        }

        return new EditLoginResult(user.DisplayName);
    }
}

/// <param name="Email">Shown, never edited: the email is the login's identity.</param>
/// <param name="LastOwner">True when this is the salon's only active owner login: the form offers no demotion and no switch-off.</param>
public sealed record EditLoginViewModel(Guid UserId, string DisplayName, string Email, Role Role, bool IsActive, bool LastOwner, string? Password = null);

/// <summary>
/// <c>/admin/settings/team/{id}</c>, owner only (ADR 0076): one login's role, password and state. Plain
/// post-redirect-get back to the team list; a refused change re-renders with the reason by its field.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/team")]
public sealed class EditLoginController(IDispatcher dispatcher, IUserService users, ILocalizationService localizer) : SalonControllerBase
{
    private const string ListPath = "/admin/settings/team";

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken cancellationToken)
        => await ModelAsync(id, cancellationToken) is { } model ? View(model) : NotFound();

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, EditLoginViewModel form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        EditLoginResult? result;
        try
        {
            result = await dispatcher.SendAsync(new EditLoginCommand(id, form.Role, form.Password, form.IsActive), cancellationToken);
        }
        catch (ValidationException ex)
        {
            if (await ModelAsync(id, cancellationToken) is not { } fresh)
            {
                return NotFound();
            }

            ModelState.AddValidationFailures(ex.Errors);
            return View(fresh with { Role = form.Role, IsActive = form.IsActive });
        }

        if (result is null)
        {
            return NotFound();
        }

        SetFlash(localizer.Format("Identity.Team.Changed", result.DisplayName));
        return Redirect(ListPath);
    }

    private async Task<EditLoginViewModel?> ModelAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var lastOwner = user is { IsActive: true, Role: Role.Owner }
            && (await users.ListAsync(cancellationToken)).Count(u => u.IsActive && u.Role == Role.Owner) == 1;
        return new EditLoginViewModel(user.Id, user.DisplayName, user.Email, user.Role, user.IsActive, lastOwner);
    }
}