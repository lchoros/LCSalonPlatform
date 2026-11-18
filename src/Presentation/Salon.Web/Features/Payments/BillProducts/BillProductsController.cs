using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.BillProducts;

/// <summary>
/// The products on a visit's bill (ADR 0037): <c>POST /admin/payments/checkout/{appointmentId}/products</c> rings one
/// up, <c>POST .../products/{lineId}/remove</c> takes an unpaid one off, <c>POST .../products/{lineId}/return</c>
/// gives the money back for a paid one (ADR 0040). Tenant only, behind the checkout's own
/// permissions (<see cref="Permissions.PaymentsRecord"/> and <see cref="Permissions.ClientsView"/>): whoever takes the
/// payment rings up the bottle. Every answer is a redirect to the checkout of the visit, with a confirmation or with
/// the reason nothing changed; another salon's id is a 404 like a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/checkout/{appointmentId:guid}/products")]
public sealed class BillProductsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The bill of a visit; the Checkout slice owns the route.</summary>
    public const string CheckoutPath = "/admin/payments/checkout";

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Guid appointmentId, Guid? product, int? quantity, CancellationToken cancellationToken)
    {
        try
        {
            // A product that was not chosen and a quantity that is not a number are the mistakes the service names.
            var bill = await dispatcher.SendAsync(new AddBillProductCommand(appointmentId, product ?? Guid.Empty, quantity ?? 0, CurrentUserId()), cancellationToken);
            var name = bill.Products.LastOrDefault(p => p.ProductId == product && !p.IsPaid)?.Name ?? string.Empty;
            SetFlash(localizer.Format("Payments.Checkout.Products.Added", name));
        }
        catch (ClientAccountException ex) when (ex.Refusal != ClientAccountRefusal.NotFound)
        {
            SetProblem(localizer[$"Payments.Fee.Refused.{ex.Refusal}"]);
        }
        catch (ClientAccountException)
        {
            return NotFound();
        }

        return BackToTheBill(appointmentId);
    }

    [HttpPost("{lineId:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid appointmentId, Guid lineId, CancellationToken cancellationToken)
    {
        try
        {
            await dispatcher.SendAsync(new RemoveBillProductCommand(appointmentId, lineId), cancellationToken);
            SetFlash(localizer["Payments.Checkout.Products.Removed"]);
        }
        catch (ClientAccountException ex) when (ex.Refusal != ClientAccountRefusal.NotFound)
        {
            SetProblem(localizer[$"Payments.Fee.Refused.{ex.Refusal}"]);
        }
        catch (ClientAccountException)
        {
            return NotFound();
        }

        return BackToTheBill(appointmentId);
    }

    /// <summary>
    /// A client brings back units of a paid line (ADR 0040): the money goes back with the chosen method and, when the
    /// box is ticked, the units go back on the shelf. An unticked box posts nothing, so absent means "not back on the shelf".
    /// </summary>
    [HttpPost("{lineId:guid}/return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(Guid appointmentId, Guid lineId, int? quantity, PaymentChoice? method, bool? backOnShelf, CancellationToken cancellationToken)
    {
        if (method is not { } chosen || !chosen.IsWellFormed)
        {
            SetProblem(localizer["Payments.Checkout.Validation.MethodRequired"]);
            return BackToTheBill(appointmentId);
        }

        try
        {
            await dispatcher.SendAsync(new ReturnBillProductCommand(appointmentId, lineId, quantity ?? 0, chosen, backOnShelf ?? false, CurrentUserId()), cancellationToken);
            SetFlash(localizer["Payments.Checkout.Products.Returned"]);
        }
        catch (ClientAccountException ex) when (ex.Refusal != ClientAccountRefusal.NotFound)
        {
            SetProblem(localizer[$"Payments.Fee.Refused.{ex.Refusal}"]);
        }
        catch (ClientAccountException)
        {
            return NotFound();
        }

        return BackToTheBill(appointmentId);
    }

    /// <summary>The path is built from the route's Guid, never from text the request supplied.</summary>
    private RedirectResult BackToTheBill(Guid appointmentId) => Redirect($"{CheckoutPath}/{appointmentId}");

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}