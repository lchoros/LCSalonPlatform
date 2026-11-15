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

namespace Salon.Web.Features.Payments.Checkout;

/// <summary>
/// <c>/admin/payments/checkout/{appointmentId}</c> (ADR 0024): the bill of one visit. GET shows the price, the deposit
/// that comes off it, what is left and the client's open fees; POST records that the client paid, with one method, and
/// completes an arrived visit. Nothing is charged: Phase 1 only records what the salon took itself. Tenant only, behind
/// <see cref="Permissions.PaymentsRecord"/> and <see cref="Permissions.ClientsView"/> (the page names the client). A
/// refused request shows the page again with the reason; another salon's id is a 404 like a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/checkout/{appointmentId:guid}")]
public sealed class CheckoutController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string BasePath = "/admin/payments/checkout";

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid appointmentId, CancellationToken cancellationToken)
    {
        var bill = await dispatcher.SendAsync(new CheckoutQuery(appointmentId), cancellationToken);

        // A refused change to the products on the bill comes back here with its reason (the BillProducts slice).
        return bill is null ? NotFound() : View(new CheckoutViewModel(bill, TakeFlash(), TakeProblem(), await SellableAsync(bill, cancellationToken)));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(Guid appointmentId, PaymentMethod? method, Guid[]? fees, CancellationToken cancellationToken)
    {
        string? error = null;

        // A value that did not even bind (an unknown method, a fee id that is not an id) never reaches the pipeline.
        if (!ModelState.IsValid)
        {
            error = localizer["Payments.Checkout.Validation.MethodRequired"];
        }
        else
        {
            try
            {
                await dispatcher.SendAsync(new PayBillCommand(appointmentId, method, fees ?? [], CurrentUserId()), cancellationToken);
                SetFlash(localizer["Payments.Checkout.Paid"]);
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

        var bill = await dispatcher.SendAsync(new CheckoutQuery(appointmentId), cancellationToken);
        return bill is null ? NotFound() : View("Show", new CheckoutViewModel(bill, null, error, await SellableAsync(bill, cancellationToken)));
    }

    /// <summary>Where the product forms post; the BillProducts slice owns the routes.</summary>
    public static string ProductsPath(Guid appointmentId) => $"{BasePath}/{appointmentId}/products";

    public static string RemoveProductPath(Guid appointmentId, Guid lineId) => $"{BasePath}/{appointmentId}/products/{lineId}/remove";

    /// <summary>Products are rung up only on a bill that can be paid; for any other visit the list is not read.</summary>
    private async Task<IReadOnlyList<SellableProduct>> SellableAsync(VisitBill bill, CancellationToken cancellationToken)
        => bill.CanPay ? await dispatcher.SendAsync(new SellableProductsQuery(), cancellationToken) : [];

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">A refused request, already localized.</param>
/// <param name="Sellable">The products the desk can ring up; empty when the bill cannot be paid or the salon sells none.</param>
public sealed record CheckoutViewModel(VisitBill Bill, string? Flash, string? Error, IReadOnlyList<SellableProduct> Sellable);