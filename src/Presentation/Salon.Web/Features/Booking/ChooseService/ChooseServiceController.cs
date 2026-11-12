using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Public, anonymous start of the booking flow at <c>/book</c> on the salon's host (ADR 0014): one tappable card per
/// active service, each leading to the time picker. The platform domain has no booking page.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class ChooseServiceController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var services = await dispatcher.SendAsync(new ChooseServiceQuery(), cancellationToken);

        return View(new ChooseServiceViewModel(tenant?.Name ?? string.Empty, services));
    }
}

public sealed record ChooseServiceViewModel(string SalonName, IReadOnlyList<BookableService> Services);
