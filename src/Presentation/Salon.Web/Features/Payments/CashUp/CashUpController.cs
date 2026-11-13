using System.Security.Claims;

using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using NodaTime;
using NodaTime.Text;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.CashUp;

/// <summary>
/// <c>/admin/payments/cash-up?date=YYYY-MM-DD</c> (ADR 0024): the till of one day (today in the salon's zone by
/// default). GET adds up what was recorded per method and lists every movement; POST closes the day with the cash the
/// person counted. Tenant only, behind <see cref="Permissions.PaymentsRecord"/> and
/// <see cref="Permissions.ClientsView"/> (the list names clients). A day is closed once; a second attempt and a day
/// that has not come show the page with the reason.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/cash-up")]
public sealed class CashUpController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/payments/cash-up";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? date, CancellationToken cancellationToken)
    {
        // An unparsable date falls back to today rather than failing a page opened from a bookmark.
        var day = await dispatcher.SendAsync(new CashUpQuery(ParseDate(date)), cancellationToken);
        return View(new CashUpViewModel(day, TakeFlash(), null, null, null));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(string? date, decimal? countedCash, string? note, CancellationToken cancellationToken)
    {
        if (ParseDate(date) is not { } day)
        {
            return RedirectToAction(nameof(Index));
        }

        string? error;

        // A value that did not even bind (letters in the amount) never reaches the pipeline.
        if (!ModelState.IsValid)
        {
            error = localizer["Payments.CashUp.Validation.Counted"];
        }
        else
        {
            try
            {
                var result = await dispatcher.SendAsync(new CloseCashUpCommand(day, countedCash, note, CurrentUserId()), cancellationToken);
                if (result.Outcome == CashUpOutcome.Closed)
                {
                    SetFlash(localizer["Payments.CashUp.Closed"]);
                    return RedirectToAction(nameof(Index), new { date = LocalDatePattern.Iso.Format(day) });
                }

                error = localizer[$"Payments.CashUp.Refused.{result.Outcome}"];
            }
            catch (ValidationException ex)
            {
                error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage));
            }
        }

        var current = await dispatcher.SendAsync(new CashUpQuery(day), cancellationToken);
        return View("Index", new CashUpViewModel(current, null, error, countedCash, note));
    }

    private static LocalDate? ParseDate(string? date)
        => date is { Length: > 0 } && LocalDatePattern.Iso.Parse(date.Trim()) is { Success: true } parsed ? parsed.Value : null;

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">A refused close, already localized.</param>
/// <param name="CountedCash">What the person typed, to show again after a refusal.</param>
public sealed record CashUpViewModel(CashDay Day, string? Flash, string? Error, decimal? CountedCash, string? Note);