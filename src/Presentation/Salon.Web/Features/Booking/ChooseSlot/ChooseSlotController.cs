using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.ChooseSlot;

/// <summary>
/// Step 2 at <c>/book/{serviceId}?date=yyyy-MM-dd&amp;staff={id}&amp;start={instant}</c> (ADR 0014): a person picker
/// that has to be answered first where more than one person offers the service ("anyone available" is an answer,
/// <c>staff=any</c>), a date strip, and the free times of the chosen day. Tapping a time selects it on
/// this same page and the Continue button carries it to the details form (the owner's call, 2026-11-20) — all plain
/// links, so the page works without JavaScript. An unknown day, person or start falls back quietly.
/// </summary>
[RequireTenant]
[Route("book")]
public sealed class ChooseSlotController(IDispatcher dispatcher, ITenantContext tenantContext, ITenantLookup tenantLookup, IVisitBasket basket) : SalonControllerBase
{
    [HttpGet("{serviceId:guid}")]
    public async Task<IActionResult> Index(Guid serviceId, string? date, string? staff, string? from, string? start, string? also, CancellationToken cancellationToken)
    {
        // "any" is a choice the visitor made; nothing at all is no choice yet (ADR 0094, stage 3).
        var anyone = string.Equals(staff, ChooseSlotQuery.AnyStaff, StringComparison.OrdinalIgnoreCase);
        Guid? staffId = Guid.TryParse(staff, out var id) ? id : null;
        var result = await dispatcher.SendAsync(new ChooseSlotQuery(serviceId, ParseDate(date), staffId, ParseDate(from), ParseStart(start), anyone, basket.Read(also, serviceId)), cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        return View(new ChooseSlotViewModel(tenant?.Name ?? string.Empty, result)
        {
            AlsoQuery = VisitBasket.Query(Extras(result)),
            AddPath = basket.IsOpen && !result.NeedsStaffChoice && result.Lines.Count < VisitRules.MaxServices ? AddPath(result) : null,
            RemovePaths = result.Lines.Count > 1 ? result.Lines.ToDictionary(l => l.ServiceId, l => WithoutPath(result, l.ServiceId)) : [],
        });
    }

    private static IEnumerable<Guid> Extras(ChooseSlotResult result) => result.Lines.Skip(1).Select(l => l.ServiceId);

    /// <summary>The answer to "who with" as the address carries it: a person, "any", or nothing while it is unanswered.</summary>
    private static string? StaffValue(ChooseSlotResult result)
        => result.SelectedStaffId is { } person ? person.ToString() : result.AnyoneChosen && result.Staff.Count > 1 ? ChooseSlotQuery.AnyStaff : null;

    /// <summary>The service list in its "add to this booking" mode (ADR 0095); the ChooseService slice owns the route.</summary>
    private static string AddPath(ChooseSlotResult result)
        => ListUrl.Build(PublicBookingDefaults.BookPath, ("add", result.ServiceId.ToString()), (VisitBasket.Parameter, VisitBasket.Write(Extras(result))), ("staff", StaffValue(result)));

    /// <summary>This page without one of its services: the next one takes the first place when the first is taken out.</summary>
    private static string WithoutPath(ChooseSlotResult result, Guid removed)
    {
        var left = result.Lines.Select(l => l.ServiceId).Where(id => id != removed).ToList();
        return ListUrl.Build($"{PublicBookingDefaults.BookPath}/{left[0]}", (VisitBasket.Parameter, VisitBasket.Write(left.Skip(1))), ("staff", StaffValue(result)));
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

public sealed record ChooseSlotViewModel(string SalonName, ChooseSlotResult Result)
{
    /// <summary>The further services of the visit as every link on the page carries them: <c>&amp;also=…</c>, or nothing.</summary>
    public string AlsoQuery { get; init; } = string.Empty;

    /// <summary>Where "Add another service" leads; null when it is not offered (switched off, the question "who with" still open, or the visit is full).</summary>
    public string? AddPath { get; init; }

    /// <summary>For a visit of several services, the page without each one of them; empty for one service.</summary>
    public IReadOnlyDictionary<Guid, string> RemovePaths { get; init; } = new Dictionary<Guid, string>();
}