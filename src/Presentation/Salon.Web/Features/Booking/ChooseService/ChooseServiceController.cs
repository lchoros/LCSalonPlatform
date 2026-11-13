using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ChooseService;

/// <summary>
/// Public, anonymous start of the booking flow at <c>/book</c> on the salon's host (ADR 0014): one tappable card per
/// active service, grouped by category, each leading to the time picker. The platform domain has no booking page.
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
        var groups = await dispatcher.SendAsync(new ChooseServiceQuery(), cancellationToken);

        return View(new ChooseServiceViewModel(tenant?.Name ?? string.Empty, groups));
    }
}

/// <param name="Groups">Active services grouped by category in display order; empty shows the empty state.</param>
public sealed record ChooseServiceViewModel(string SalonName, IReadOnlyList<BookableServiceGroup> Groups)
{
    /// <summary>Headings appear only once the salon has filed at least one service under a category.</summary>
    public bool ShowHeadings => Groups.Any(g => g.CategoryName is not null);
}