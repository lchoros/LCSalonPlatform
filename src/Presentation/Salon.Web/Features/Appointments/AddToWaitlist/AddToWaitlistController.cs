using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.AddToWaitlist;

/// <summary>
/// The form that puts a client on the waitlist, <c>/admin/appointments/waitlist/new?clientId=&amp;serviceId=&amp;staffId=</c>
/// (ADR 0041): the client is given, the desk picks the service, the person if it matters, the days and a remark.
/// Tenant only, for signed-in users with <see cref="Permissions.BookingsManage"/> and
/// <see cref="Permissions.ClientsView"/>. Reached from the client card and from the booking form when a day has no
/// free time. A saved entry lands on the list; a refused one shows the form again with the reason.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments/waitlist/new")]
public sealed class AddToWaitlistController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The list; the Waitlist slice owns the route.</summary>
    public const string ListPath = "/admin/appointments/waitlist";

    [HttpGet("")]
    public async Task<IActionResult> New(Guid? clientId, Guid? serviceId, Guid? staffId, CancellationToken cancellationToken)
    {
        var form = clientId is { } id ? await dispatcher.SendAsync(new WaitlistFormQuery(id), cancellationToken) : null;
        return form is null ? NotFound() : View(new AddToWaitlistViewModel(form, serviceId, staffId, null, null, null, null));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(Guid? clientId, Guid? serviceId, Guid? staffId, string? notBefore, string? notAfter, string? note, CancellationToken cancellationToken)
    {
        var form = clientId is { } id ? await dispatcher.SendAsync(new WaitlistFormQuery(id), cancellationToken) : null;
        if (form is null)
        {
            return NotFound();
        }

        // A date field that holds text which is not a date is the same mistake as days in the wrong order.
        var first = ParseDay(notBefore, out var firstOk);
        var last = ParseDay(notAfter, out var lastOk);
        var outcome = serviceId is null
            ? WaitlistAddOutcome.ServiceUnknown
            : !firstOk || !lastOk
                ? WaitlistAddOutcome.DaysInvalid
                : await dispatcher.SendAsync(new AddToWaitlistCommand(form.ClientId, serviceId.Value, staffId, first, last, note, CurrentUserId()), cancellationToken);

        if (outcome is WaitlistAddOutcome.Added or WaitlistAddOutcome.Updated)
        {
            SetFlash(localizer.Format($"Appointments.Waitlist.{outcome}", form.ClientName));
            return Redirect(ListPath);
        }

        if (outcome == WaitlistAddOutcome.ClientUnknown)
        {
            return NotFound();
        }

        return View(new AddToWaitlistViewModel(form, serviceId, staffId, notBefore, notAfter, note, localizer[$"Appointments.Waitlist.Refused.{outcome}"]));
    }

    /// <summary>Blank is "no day"; anything else must be an ISO date, as a date field posts it.</summary>
    private static LocalDate? ParseDay(string? iso, out bool ok)
    {
        ok = true;
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        var parsed = LocalDatePattern.Iso.Parse(iso.Trim());
        ok = parsed.Success;
        return parsed.Success ? parsed.Value : null;
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">Why the entry was refused, already localized.</param>
public sealed record AddToWaitlistViewModel(WaitlistForm Form, Guid? ServiceId, Guid? StaffMemberId, string? NotBefore, string? NotAfter, string? Note, string? Error);