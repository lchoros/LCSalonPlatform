using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Services.Booking;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Booking.Reschedule;

/// <summary>
/// "Change the time" behind the client's own link (ADR 0021): the same secret token as the cancel link, the same engine
/// as a new booking, and the visit keeps its current time until another one is chosen.
/// </summary>
[RequireTenant]
[Route("book/change")]
public sealed class RescheduleController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("{token}")]
    public async Task<IActionResult> Change(string token, string? date, string? staff, CancellationToken cancellationToken)
    {
        var options = await dispatcher.SendAsync(new RescheduleQuery(token, ParseDate(date), staff), cancellationToken);
        return View(new RescheduleViewModel(token, options, null));
    }

    [HttpPost("{token}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeConfirmed(string token, string? start, Guid? staffMemberId, CancellationToken cancellationToken)
    {
        var newStart = ParseInstant(start);
        if (newStart is null)
        {
            return RedirectToAction(nameof(Change), new { token });
        }

        var result = await dispatcher.SendAsync(new RescheduleCommand(token, staffMemberId, newStart.Value), cancellationToken);
        if (result.Outcome == PublicRescheduleOutcome.Moved)
        {
            var current = await dispatcher.SendAsync(new RescheduleQuery(token, null, null), cancellationToken);
            return View("Change", new RescheduleViewModel(token, current with { State = RescheduleState.Moved }, null));
        }

        // Taken meanwhile: the same page again with fresh times and a message. Every other outcome is a state of its own.
        var staff = staffMemberId is { } id ? id.ToString() : RescheduleQuery.AnyStaff;
        var options = await dispatcher.SendAsync(new RescheduleQuery(token, null, staff), cancellationToken);
        var message = result.Outcome == PublicRescheduleOutcome.SlotTaken ? localizer["Booking.Public.SlotTaken"] : null;
        return View("Change", new RescheduleViewModel(token, options, message));
    }

    private static LocalDate? ParseDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        var parsed = LocalDatePattern.Iso.Parse(date.Trim());
        return parsed.Success ? parsed.Value : null;
    }

    private static Instant? ParseInstant(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
        {
            return null;
        }

        var parsed = InstantPattern.ExtendedIso.Parse(start.Trim());
        return parsed.Success ? parsed.Value : null;
    }
}

/// <param name="Message">Shown above the times, for example when the chosen one was taken meanwhile.</param>
public sealed record RescheduleViewModel(string Token, RescheduleOptions Options, string? Message);