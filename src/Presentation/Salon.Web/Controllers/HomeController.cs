using System.Diagnostics;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Models;

namespace Salon.Web.Controllers;

public sealed class HomeController(ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var tenant = tenantContext.TenantId is { } id
            ? await tenantLookup.FindByIdAsync(id, cancellationToken)
            : null;

        return View(new HomeViewModel(tenant?.Name));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
        => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}