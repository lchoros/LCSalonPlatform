using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ListServices;

/// <summary>
/// Public price list at <c>/services</c>: the first page a client reaches from the salon's home page. Also the landing
/// page after an admin saves a service, so it shows the flash left by that redirect.
/// </summary>
[RequireTenant]
[Route("services")]
public sealed class ListServicesController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var services = await dispatcher.SendAsync(new ListServicesQuery(), cancellationToken);

        return View(new ListServicesViewModel(tenant?.Name ?? string.Empty, services, TakeFlash()));
    }
}

/// <param name="SalonName">Heading of the page.</param>
/// <param name="Services">Active services in display order; empty shows the empty state.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ListServicesViewModel(string SalonName, IReadOnlyList<ServiceListItem> Services, string? Flash);