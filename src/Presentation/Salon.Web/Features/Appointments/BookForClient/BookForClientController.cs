using System.Globalization;
using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Appointments.BookForClient;

/// <summary>
/// The admin "book for a client" page at <c>/admin/appointments/new</c>. One page, plain GET round trips: searching a client,
/// choosing one, picking a service, a staff member (or any) and a date all reload the page with the selection in the query
/// string, and the slot buttons are small POST forms. Tenant only, for signed-in users with
/// <see cref="Permissions.BookingsManage"/> and <see cref="Permissions.ClientsView"/> (the picker shows client details,
/// ADR 0009, ADR 0011). A booked slot lands on that day's list with a confirmation; a slot taken meanwhile re-renders the
/// page with a message and fresh slots (ADR 0012).
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.BookingsManage)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/appointments")]
public sealed class BookForClientController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string FormPath = "/admin/appointments/new";
    private const string ListPath = "/admin/appointments";

    [HttpGet("new")]
    public async Task<IActionResult> New(string? q, Guid? clientId, Guid? serviceId, Guid? staffId, string? date, CancellationToken cancellationToken)
    {
        var query = q?.Trim() is { Length: > 0 } text ? text[..Math.Min(text.Length, 100)] : null;
        var form = await dispatcher.SendAsync(new BookForClientFormQuery(query, clientId, serviceId, staffId, ParseDate(date)), cancellationToken);
        return View(new BookForClientViewModel(form, null));
    }

    [HttpPost("new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(BookForClientCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { ByUserId = CurrentUserId() };

        // A value that did not even bind is reported by MVC; the rules themselves run in the pipeline.
        if (!ModelState.IsValid)
        {
            return await RenderAgainAsync(command, null, cancellationToken);
        }

        BookForClientResult result;
        try
        {
            result = await dispatcher.SendAsync(command, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderAgainAsync(command, null, cancellationToken);
        }
        catch (SlotNoLongerAvailableException)
        {
            return await RenderAgainAsync(command, localizer["Appointments.Book.SlotTaken"], cancellationToken);
        }
        catch (InvalidOperationException)
        {
            // The service went inactive or the staff member stopped being bookable between the page and the post.
            return await RenderAgainAsync(command, localizer["Appointments.Book.Unavailable"], cancellationToken);
        }

        var culture = CultureInfo.CurrentCulture;
        SetFlash(localizer.Format("Appointments.Book.Booked", result.ClientName, result.Start.Date.ToString("d", culture), result.Start.TimeOfDay.ToString("t", culture)));
        return Redirect($"{ListPath}?date={LocalDatePattern.Iso.Format(result.Start.Date)}");
    }

    /// <summary>The same selection with fresh slots; the chosen client, service, staff (or any) and day survive the round trip.</summary>
    private async Task<IActionResult> RenderAgainAsync(BookForClientCommand command, string? error, CancellationToken cancellationToken)
    {
        var form = await dispatcher.SendAsync(
            new BookForClientFormQuery(null, command.ClientId, command.ServiceId, command.AnyStaff ? null : command.StaffMemberId, ParseDate(command.Date)),
            cancellationToken);
        return View(new BookForClientViewModel(form, error));
    }

    /// <summary>An absent or unparsable date means today in the salon's zone (the handler decides what "today" is).</summary>
    private static LocalDate? ParseDate(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return null;
        }

        var result = LocalDatePattern.Iso.Parse(iso.Trim());
        return result.Success ? result.Value : null;
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}