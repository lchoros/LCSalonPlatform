using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Tenancy;
using Salon.Services.Tenancy;
using Salon.Web.Framework.Booking;
using Salon.Web.Framework.Booking.Times;
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
    public async Task<IActionResult> Index(Guid serviceId, string? date, string? staff, string? from, string? start, string? also, string? with, CancellationToken cancellationToken)
    {
        // "any" is a choice the visitor made; nothing at all is no choice yet (ADR 0094, stage 3).
        var anyone = string.Equals(staff, ChooseSlotQuery.AnyStaff, StringComparison.OrdinalIgnoreCase);
        Guid? staffId = Guid.TryParse(staff, out var id) ? id : null;
        var extras = basket.Read(also, serviceId);
        var result = await dispatcher.SendAsync(
            new ChooseSlotQuery(serviceId, ParseDate(date), staffId, ParseDate(from), ParseStart(start), anyone, extras, basket.ReadStaff(with, extras.Count)),
            cancellationToken);
        if (result is null)
        {
            return NotFound();
        }

        // [RequireTenant] has already turned a tenant-less request into a 404.
        var tenant = await tenantLookup.FindByIdAsync(tenantContext.TenantId!.Value, cancellationToken);
        var services = result.Lines.Select(l => l.ServiceId).ToList();
        var answers = result.Lines.Select(Answer).ToList();
        var canAdd = basket.IsOpen && result.Lines.Count < VisitRules.MaxServices && result.Slots.Count > 0;
        return View(new ChooseSlotViewModel(tenant?.Name ?? string.Empty, result)
        {
            AlsoQuery = VisitBasket.Query(services.Skip(1), answers.Skip(1)),
            // One service is picked in full — its time and who with — before another is added (the owner, 2026-11-25);
            // who with and the day ride along. The time does not (2026-11-26): with another service the visit is a
            // different length, so the client chooses its time again.
            AddPath = canAdd && result.Chosen is not null && !result.NeedsStaffChoice
                ? ListUrl.Build(PublicBookingDefaults.BookPath, [("add", services[0].ToString()), .. Carried(result, services, answers).Where(kept => kept.Name != "start")])
                : null,
            AddAfterTime = canAdd && (result.Chosen is null || result.NeedsStaffChoice),
            RemovePaths = result.Lines.Count > 1
                ? result.Lines.Select((l, i) => (l.ServiceId, Path: Address(result, Without(services, i), Without(answers, i)))).ToDictionary(p => p.ServiceId, p => p.Path)
                : [],
            // An answer to "who with" for one service: this page with that answer in its place, the rest as it is.
            StaffPath = (line, answer) => Address(result, services, [.. answers.Select((a, i) => i == line ? answer : a)]),
        });
    }

    /// <summary>The answer to "who with" for one service as the address carries it: a person, "any", or nothing while it is owed.</summary>
    private static string? Answer(VisitLine line)
        => line.StaffId is { } person ? person.ToString() : line.Anyone && line.Staff.Count > 1 ? ChooseSlotQuery.AnyStaff : null;

    private static List<T> Without<T>(List<T> list, int index) => [.. list.Where((_, i) => i != index)];

    /// <summary>This page for the given services and answers, with the day and the tapped time kept.</summary>
    private static string Address(ChooseSlotResult result, List<Guid> services, List<string?> answers)
        => ListUrl.Build($"{PublicBookingDefaults.BookPath}/{services[0]}", [.. Carried(result, services, answers)]);

    /// <summary>Everything but the first service: who does it, the further services and who does those, the day, the window and the time.</summary>
    private static IEnumerable<(string Name, string? Value)> Carried(ChooseSlotResult result, List<Guid> services, List<string?> answers) =>
    [
        ("staff", answers[0]),
        (VisitBasket.Parameter, VisitBasket.Write(services.Skip(1))),
        (VisitBasket.StaffParameter, VisitBasket.WriteStaff(answers.Skip(1))),
        ("date", LocalDatePattern.Iso.Format(result.SelectedDate)),
        ("from", LocalDatePattern.Iso.Format(result.Days.Count > 0 ? result.Days[0].Date : result.SelectedDate)),
        ("start", result.Chosen is { } chosen ? InstantPattern.ExtendedIso.Format(chosen.Start) : null),
    ];

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

    /// <summary>"Add another service" would be offered, but a time has to be chosen first.</summary>
    public bool AddAfterTime { get; init; }

    /// <summary>For a visit of several services, the page without each one of them; empty for one service.</summary>
    public IReadOnlyDictionary<Guid, string> RemovePaths { get; init; } = new Dictionary<Guid, string>();

    /// <summary>This page with one service's "who with" answered: the line (0 is the first service) and the answer, a person's id or "any" (ADR 0096).</summary>
    public Func<int, string, string> StaffPath { get; init; } = (_, _) => string.Empty;

    /// <summary>The staff line of the first service whose "who with" is still owed: what a too-early Continue points at.</summary>
    public string FirstOpenStaffLine => $"staff-line-{Math.Max(0, Result.Lines.ToList().FindIndex(l => l.NeedsStaffChoice))}";
}