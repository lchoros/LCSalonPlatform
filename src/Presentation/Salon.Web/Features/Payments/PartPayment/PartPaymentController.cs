using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.PartPayment;

/// <summary>
/// A part of a visit's price paid one way before the rest is paid another (ADR 0061). A command: the line of money
/// commits or nothing does.
/// </summary>
/// <param name="Amount">Above zero and less than what is left to pay for the visit.</param>
/// <param name="PartId">The id the form was given; the same form sent twice is one part.</param>
public sealed record PayPartCommand(Guid AppointmentId, decimal Amount, PaymentChoice Method, Guid? PartId, Guid? ByUserId) : ICommand<VisitBill>;

/// <summary>Hands the part to <see cref="IClientAccountService"/>, which owns the bill; nothing about money is decided in a slice.</summary>
public sealed class PayPartHandler(IClientAccountService account) : IRequestHandler<PayPartCommand, VisitBill>
{
    public Task<VisitBill> HandleAsync(PayPartCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return account.PayPartAsync(request.AppointmentId, request.Amount, request.Method, request.ByUserId, request.PartId, cancellationToken);
    }
}

/// <summary>
/// <c>POST /admin/payments/checkout/{appointmentId}/part</c> (ADR 0061): "£20 in cash and the rest by card". Tenant
/// only, behind the checkout's own permissions (<see cref="Permissions.PaymentsRecord"/> and
/// <see cref="Permissions.ClientsView"/>). Every answer is a redirect to the bill, with what was recorded and what is
/// left, or with the reason nothing changed.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/checkout/{appointmentId:guid}/part")]
public sealed class PartPaymentController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The bill of a visit; the Checkout slice owns the route.</summary>
    public const string CheckoutPath = "/admin/payments/checkout";

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(Guid appointmentId, decimal? amount, PaymentChoice? method, Guid? part, CancellationToken cancellationToken)
    {
        // Letters where the amount goes, or no method: said our way, and nothing is recorded.
        if (!ModelState.IsValid || amount is not { } value || method is not { } chosen)
        {
            SetProblem(localizer["Payments.Checkout.Part.Refused.Incomplete"]);
            return Redirect($"{CheckoutPath}/{appointmentId}");
        }

        try
        {
            var bill = await dispatcher.SendAsync(new PayPartCommand(appointmentId, value, chosen, part, CurrentUserId()), cancellationToken);
            var culture = CultureInfo.CurrentCulture;
            SetFlash(localizer.Format("Payments.Checkout.Part.Recorded", (bill.Price with { Amount = value }).ToDisplayString(culture), bill.Outstanding.ToDisplayString(culture)));
        }
        catch (ClientAccountException ex) when (ex.Refusal == ClientAccountRefusal.NotFound)
        {
            return NotFound();
        }
        catch (ClientAccountException ex)
        {
            SetProblem(localizer[$"Payments.Fee.Refused.{ex.Refusal}"]);
        }

        return Redirect($"{CheckoutPath}/{appointmentId}");
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}