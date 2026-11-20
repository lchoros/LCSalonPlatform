using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// Step 2 at <c>/book/{serviceId}?date=yyyy-MM-dd&amp;staff={id}&amp;start={instant}</c> (ADR 0014): a date strip, a
/// person picker with "anyone available" first, and the free times of the chosen day. Tapping a time selects it on
/// this same page and the Continue button carries it to the details form (the owner's call, 2026-11-20) — all plain
/// links, so the page works without JavaScript. An unknown day, person or start falls back quietly.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class ChooseSlotController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup) : SalonControllerBase
{
    [HttpGet("{serviceId:guid}")]
    public async Task<IActionResult> Index(Guid serviceId, string? date, Guid? staff, string? from, string? start, CancellationToken cancellationToken)
    {
        var result = await dispatcher.SendAsync(new ChooseSlotQuery(serviceId, ParseDate(date), staff, ParseDate(from), ParseStart(start)), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        return View(new ChooseSlotViewModel(tenant?.Name ?? string.Empty, result));
    }

    private static LocalDate? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        var parsed = LocalDatePattern.Iso.Parse(iso.Trim());
        return parsed.Success ? parsed.Value : null;
    }

    private static Instant? ParseStart(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        var parsed = InstantPattern.ExtendedIso.Parse(iso.Trim());
        return parsed.Success ? parsed.Value : null;
    }
}

public sealed record ChooseSlotViewModel(string SalonName, ChooseSlotResult Result);