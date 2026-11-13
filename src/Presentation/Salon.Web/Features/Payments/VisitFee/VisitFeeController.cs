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

namespace Salon.Web.Features.Payments.VisitFee;

/// <summary>
/// <c>/admin/payments/fees/{appointmentId}</c> (ADR 0022): the fee of one missed or late-cancelled visit. GET previews
/// what the salon's cancellation policy allows and shows the fee on the client's account with who did what; POST adds
/// the fee, <c>POST …/paid</c> records that the client paid it at the salon, <c>POST …/waive</c> lets it go with a
/// reason. Nothing is charged: Phase 1 only records. Tenant only, behind <see cref="Permissions.PaymentsRecord"/> and
/// <see cref="Permissions.ClientsView"/> (the page names the client). A refused request shows the page again with the
/// reason; another salon's id is a 404 like a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/fees/{appointmentId:guid}")]
public sealed class VisitFeeController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string BasePath = "/admin/payments/fees";

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid appointmentId, CancellationToken cancellationToken)
    {
        var visit = await dispatcher.SendAsync(new VisitFeeQuery(appointmentId), cancellationToken);
        return visit is null ? NotFound() : View(new VisitFeeViewModel(visit, TakeFlash(), null));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Charge(Guid appointmentId, decimal? amount, CancellationToken cancellationToken)
        => ActAsync(appointmentId, new ChargeFeeCommand(appointmentId, amount, CurrentUserId()), "Payments.Fee.Charged", "Payments.Fee.Validation.Amount", cancellationToken);

    [HttpPost("paid")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Paid(Guid appointmentId, PaymentMethod? method, CancellationToken cancellationToken)
        => ActAsync(appointmentId, new RecordFeePaymentCommand(appointmentId, method, CurrentUserId()), "Payments.Fee.PaymentRecorded", "Payments.Fee.Validation.MethodRequired", cancellationToken);

    [HttpPost("waive")]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Waive(Guid appointmentId, string? reason, CancellationToken cancellationToken)
        => ActAsync(appointmentId, new WaiveFeeCommand(appointmentId, reason, CurrentUserId()), "Payments.Fee.WaivedDone", "Payments.Fee.Validation.ReasonRequired", cancellationToken);

    /// <summary>Sends the command; success goes back to the page with a confirmation, a refusal shows the page with the reason.</summary>
    private async Task<IActionResult> ActAsync(Guid appointmentId, ICommand<FeeLine> command, string doneKey, string invalidKey, CancellationToken cancellationToken)
    {
        string? error = null;

        // A value that did not even bind (letters in the amount, an unknown method) never reaches the pipeline.
        if (!ModelState.IsValid)
        {
            error = localizer[invalidKey];
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

        var visit = await dispatcher.SendAsync(new VisitFeeQuery(appointmentId), cancellationToken);
        return visit is null ? NotFound() : View("Show", new VisitFeeViewModel(visit, null, error));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">A refused request, already localized.</param>
public sealed record VisitFeeViewModel(AppointmentFee Visit, string? Flash, string? Error);