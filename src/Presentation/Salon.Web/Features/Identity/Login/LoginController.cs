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

namespace Salon.Web.Features.Identity.Login;

/// <summary>
/// Sign-in form at <c>/account/login</c> on a salon's own host (ADR 0009); the platform domain has no login. A valid post
/// issues the cookie and follows a <em>local</em> return URL, otherwise lands on the public price list. A failed post
/// re-renders with one generic message; only a lockout reads differently. The POST is rate-limited per address.
/// </summary>
[RequireTenant]
[Route("account")]
public sealed class LoginController(IDispatcher dispatcher, ITenantContext tenantContext, ILocalizationService localizer) : SalonControllerBase
{
    private const string DefaultLanding = "/services";

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(SafeReturnUrl(returnUrl));
        }

        return View(new LoginForm(string.Empty, string.Empty, returnUrl));
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(SalonAuthenticationDefaults.LoginRateLimitPolicy)]
    public async Task<IActionResult> Login(LoginForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var redacted = form with { Password = string.Empty };

        try
        {
            var result = await dispatcher.SendAsync(new LoginCommand(form.Email, form.Password), cancellationToken);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(
                    string.Empty,
                    result.LockedOut
                        ? localizer.Format("Identity.Login.LockedOut", (int)LockoutPolicy.LockoutDuration.TotalMinutes)
                        : localizer["Identity.Login.Failed"]);
                return View(redacted);
            }

            // [RequireTenant] has already refused tenant-less requests; this only narrows the type.
            var tenantId = tenantContext.TenantId ?? throw new TenantViolationException("Sign-in requires a tenant in scope.");
            await HttpContext.SignInAsync(
                SalonAuthenticationDefaults.Scheme,
                ClaimsFactory.Create(result.User!, tenantId),
                new AuthenticationProperties { IsPersistent = false });
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(redacted);
        }

        return LocalRedirect(SafeReturnUrl(form.ReturnUrl));
    }

    /// <summary>Only a path on this host is followed; anything absolute, protocol-relative or odd goes to the default landing.</summary>
    private string SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : DefaultLanding;
}