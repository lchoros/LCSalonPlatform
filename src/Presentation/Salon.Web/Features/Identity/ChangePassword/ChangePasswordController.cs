using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Identity;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.ChangePassword;

/// <summary>
/// A person changes their own password (ADR 0077): the current one proves it is them, the new one replaces it.
/// The wrong current password counts toward the lockout exactly like a failed sign-in — someone at an unlocked
/// screen gets five guesses, not an evening — so the handler reports failure in the result instead of throwing,
/// which would roll the count back with the transaction.
/// </summary>
public sealed record ChangePasswordCommand(Guid UserId, string CurrentPassword, string NewPassword) : ICommand<ChangePasswordOutcome>;

public enum ChangePasswordOutcome
{
    Changed = 1,
    CurrentWrong = 2,
    LockedOut = 3,
}

/// <summary>Input rules for <see cref="ChangePasswordCommand"/>; whether the current password is right needs the salon and lives in the handler.</summary>
public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    /// <summary>The same cap the team pages set; a slice never references another slice, so each names it.</summary>
    public const int MaxPasswordLength = 128;

    public ChangePasswordValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.CurrentPassword)
            .Must(password => !string.IsNullOrEmpty(password))
            .WithMessage(_ => localizer["Identity.Password.Validation.CurrentRequired"]);

        RuleFor(c => c.NewPassword)
            .Must(password => password is { Length: >= IUserService.MinPasswordLength })
            .WithMessage(_ => localizer.Format("Identity.Password.Validation.NewTooShort", IUserService.MinPasswordLength))
            .Must(password => password is null || password.Length <= MaxPasswordLength)
            .WithMessage(_ => localizer.Format("Identity.Password.Validation.NewTooLong", MaxPasswordLength))
            .Must((command, password) => !string.Equals(password, command.CurrentPassword, StringComparison.Ordinal))
            .WithMessage(_ => localizer["Identity.Password.Validation.SameAsCurrent"]);
    }
}

public sealed class ChangePasswordHandler(IUserService users, ISignInService signIn) : IRequestHandler<ChangePasswordCommand, ChangePasswordOutcome>
{
    public async Task<ChangePasswordOutcome> HandleAsync(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var user = await users.FindByIdAsync(request.UserId, cancellationToken)
            ?? throw new InvalidOperationException($"Signed-in user {request.UserId} was not found in the current tenant.");

        // The same gate as the login form: lockout first, then the password; a failure counts, a success clears.
        var attempt = await signIn.AttemptAsync(user.Email, request.CurrentPassword, cancellationToken);
        if (attempt.LockedOut)
        {
            return ChangePasswordOutcome.LockedOut;
        }

        if (!attempt.Succeeded)
        {
            return ChangePasswordOutcome.CurrentWrong;
        }

        await users.SetPasswordAsync(user.Id, request.NewPassword, cancellationToken);
        return ChangePasswordOutcome.Changed;
    }
}

/// <summary>The posted form; the confirmation never leaves the page and the command never carries it.</summary>
/// <param name="Flash">The confirmation left by a change the page redirected back from; never posted.</param>
public sealed record ChangePasswordForm(string CurrentPassword, string NewPassword, string ConfirmPassword, string? Flash = null)
{
    public static ChangePasswordForm Empty { get; } = new(string.Empty, string.Empty, string.Empty);
}

/// <summary>
/// <c>/account/password</c>, any signed-in user (ADR 0077): their own password, nobody else's. A successful change
/// rotates the security stamp — every other session ends — and re-issues this session's cookie with the fresh stamp,
/// so the device in hand stays signed in. The POST shares the login form's per-address rate limit.
/// </summary>
[RequireTenant]
[RequireSignedIn]
[Route("account")]
public sealed class ChangePasswordController(
    IDispatcher dispatcher,
    IUserService users,
    ITenantContext tenantContext,
    ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("password")]
    public IActionResult Index() => View(ChangePasswordForm.Empty with { Flash = TakeFlash() });

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(SalonAuthenticationDefaults.LoginRateLimitPolicy)]
    public async Task<IActionResult> Index(ChangePasswordForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (!string.Equals(form.NewPassword, form.ConfirmPassword, StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(ChangePasswordForm.ConfirmPassword), localizer["Identity.Password.Validation.ConfirmMismatch"]);
        }

        if (!ModelState.IsValid)
        {
            return View(ChangePasswordForm.Empty);
        }

        // [RequireSignedIn] has already admitted only a signed-in user of this tenant; these only narrow the types.
        if (!Guid.TryParse(User.FindFirst(UserClaimTypes.Subject)?.Value, out var userId))
        {
            return Challenge(SalonAuthenticationDefaults.Scheme);
        }

        var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Changing a password requires a tenant in scope.");

        ChangePasswordOutcome outcome;
        try
        {
            outcome = await dispatcher.SendAsync(new ChangePasswordCommand(userId, form.CurrentPassword, form.NewPassword), cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(ChangePasswordForm.Empty);
        }

        switch (outcome)
        {
            case ChangePasswordOutcome.LockedOut:
                ModelState.AddModelError(
                    nameof(ChangePasswordForm.CurrentPassword),
                    localizer.Format("Identity.Login.LockedOut", (int)LockoutPolicy.LockoutDuration.TotalMinutes));
                return View(ChangePasswordForm.Empty);
            case ChangePasswordOutcome.CurrentWrong:
                ModelState.AddModelError(nameof(ChangePasswordForm.CurrentPassword), localizer["Identity.Password.Validation.CurrentWrong"]);
                return View(ChangePasswordForm.Empty);
            default:
                break;
        }

        // The stamp just rotated; without a fresh cookie this very session would be the first one signed out.
        var fresh = await users.FindByIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"Signed-in user {userId} was not found after their password change.");
        await HttpContext.SignInAsync(
            SalonAuthenticationDefaults.Scheme,
            ClaimsFactory.Create(fresh, tenantId),
            new AuthenticationProperties { IsPersistent = false });

        SetFlash(localizer["Identity.Password.Changed"]);
        return Redirect("/account/password");
    }
}