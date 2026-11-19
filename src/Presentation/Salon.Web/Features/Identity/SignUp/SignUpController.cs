using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Notifications;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Identity.SignUp;

/// <param name="SalonName">The name as the salon typed it.</param>
/// <param name="SignInUrl">The new salon's own admin address, where the owner signs in with what they just chose.</param>
public sealed record SignUpDoneViewModel(string SalonName, string SignInUrl);

/// <summary>
/// Public sign-up at <c>/signup</c> on the bare base domain (ADR 0075): a salon and its owner's login in one form,
/// behind <c>Features:SignUp</c>. No tenant may be in scope: on a salon's own address the page does not exist. The
/// done page hands the owner their salon's sign-in address; the sign-in itself happens there, on the salon's host,
/// where the cookie belongs.
/// </summary>
[Route("signup")]
public sealed class SignUpController(
    IDispatcher dispatcher,
    IOptionsMonitor<FeaturesOptions> features,
    ITenantContext tenantContext,
    ITenantLookup tenants,
    IPublicUrlBuilder urls) : SalonControllerBase
{
    /// <summary>Where the owner lands after signing in: the checklist of what is left to set up (ADR 0068).</summary>
    public const string FirstStopPath = "/admin/settings/getting-started";

    [HttpGet("")]
    public IActionResult New() => Gate() is { } gone ? gone : View(SignUpCommand.Empty);

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(SignUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Gate() is { } gone)
        {
            return gone;
        }

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return View(command);
        }

        SignUpResult result;
        try
        {
            result = await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(command);
        }

        // The slug is public by design (it is the salon's address); nothing personal rides in the URL.
        return Redirect($"/signup/done?salon={result.Slug}");
    }

    [HttpGet("done")]
    public async Task<IActionResult> Done(string? salon, CancellationToken cancellationToken)
    {
        if (Gate() is { } gone)
        {
            return gone;
        }

        var tenant = salon is { Length: > 0 } ? await tenants.FindBySlugAsync(TenantSlugRules.Normalize(salon), cancellationToken) : null;
        return tenant is null || !tenant.IsActive
            ? NotFound()
            : View(new SignUpDoneViewModel(tenant.Name, urls.SalonUrl(tenant.Slug, FirstStopPath)));
    }

    /// <summary>The page exists only while the flag is on, and never on a salon's own address.</summary>
    private NotFoundResult? Gate() => !features.CurrentValue.SignUp || tenantContext.TenantId is not null ? NotFound() : null;
}