using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;
using Deposit = Salon.Services.Payments.VisitDeposit;

namespace Salon.Web.Features.Payments.VisitDeposit;

/// <summary>
/// <c>/admin/payments/deposits/{appointmentId}</c> (ADR 0023): the deposit of one visit. GET shows what the booking
/// asks for and what the salon received, with who recorded it; POST records a deposit the salon took itself (cash, its
/// own terminal, a transfer, a voucher), <c>POST …/return</c> records that it went back to the client. Nothing is
/// charged or refunded by the app: Phase 1 only records. Tenant only, behind <see cref="Permissions.PaymentsRecord"/>
/// and <see cref="Permissions.ClientsView"/> (the page names the client). A refused request shows the page again with
/// the reason; another salon's id is a 404 like a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/deposits/{appointmentId:guid}")]
public sealed class VisitDepositController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string BasePath = "/admin/payments/deposits";

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid appointmentId, CancellationToken cancellationToken)
    {
        var visit = await dispatcher.SendAsync(new VisitDepositQuery(appointmentId), cancellationToken);
        return visit is null ? NotFound() : View(new VisitDepositViewModel(visit, TakeFlash(), null));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Record(Guid appointmentId, decimal? amount, PaymentMethod? method, CancellationToken cancellationToken)
        => ActAsync(appointmentId, new RecordDepositCommand(appointmentId, amount, method, CurrentUserId()), "Payments.Deposit.Recorded", cancellationToken);

    [HttpPost("return")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Return(Guid appointmentId, PaymentMethod? method, CancellationToken cancellationToken)
        => ActAsync(appointmentId, new ReturnDepositCommand(appointmentId, method, CurrentUserId()), "Payments.Deposit.Returned", cancellationToken);

    /// <summary>Sends the command; success goes back to the page with a confirmation, a refusal shows the page with the reason.</summary>
    private async Task<IActionResult> ActAsync(Guid appointmentId, ICommand<DepositLine> command, string doneKey, CancellationToken cancellationToken)
    {
        string? error = null;

        // A value that did not even bind (letters in the amount, an unknown method) never reaches the pipeline.
        if (!ModelState.IsValid)
        {
            error = localizer["Payments.Deposit.Validation.Amount"];
        }
        else
        {
            try
            {
                await dispatcher.SendAsync(command, cancellationToken);
                SetFlash(localizer[doneKey]);
                return RedirectToAction(nameof(Show), new { appointmentId });
            }
            catch (ValidationException ex)
            {
                error = string.Join(" ", ex.Errors.Select(e => e.ErrorMessage));
            }
            catch (ClientAccountException ex) when (ex.Refusal != ClientAccountRefusal.NotFound)
            {
                error = localizer[$"Payments.Fee.Refused.{ex.Refusal}"];
            }
            catch (ClientAccountException)
            {
                return NotFound();
            }
        }

        var visit = await dispatcher.SendAsync(new VisitDepositQuery(appointmentId), cancellationToken);
        return visit is null ? NotFound() : View("Show", new VisitDepositViewModel(visit, null, error));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">A refused request, already localized.</param>
public sealed record VisitDepositViewModel(Deposit Visit, string? Flash, string? Error);