using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Infrastructure;
using Salon.Web.Models;

namespace Salon.Web.Controllers;

public sealed class HomeController(ITenantContext tenantContext, ITenantLookup tenantLookup, IOptionsMonitor<FeaturesOptions> features) : SalonControllerBase
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var tenant = tenantContext.TenantId is { } id
            ? await tenantLookup.FindByIdAsync(id, cancellationToken)
            : null;

        return View(new HomeViewModel(tenant?.Name, tenant is null && features.CurrentValue.SignUp));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    /// <summary>
    /// The page behind an empty error status (ADR 0087): the pipeline re-executes here for a 404, 403, 429 and the
    /// like, the status stays what it was, and a person reads a sentence instead of a blank screen. A caller that
    /// did not ask for HTML (fetch, htmx, a script) keeps the bare status it can act on.
    /// </summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int id)
    {
        if (!Request.Headers.Accept.Any(value => value?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true))
        {
            return StatusCode(id);
        }

        Response.StatusCode = id;
        return View(new StatusViewModel(StatusViewModel.KindOf(id), User.Identity?.IsAuthenticated == true));
    }
}