using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NodaTime.Text;
using Salon.Core.Appointments;
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
        return bill is null ? NotFound() : View(await ViewModelAsync(bill, TakeFlash(), TakeProblem(), cancellationToken));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(Guid appointmentId, PaymentChoice? method, Guid[]? fees, CancellationToken cancellationToken)
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
        return bill is null ? NotFound() : View("Show", await ViewModelAsync(bill, null, error, cancellationToken));
    }

    /// <summary>The booking form for a client; the BookForClient slice owns the route.</summary>
    public const string BookPath = "/admin/appointments/new";

    private async Task<CheckoutViewModel> ViewModelAsync(VisitBill bill, string? flash, string? error, CancellationToken cancellationToken)
    {
        // The next visit is offered once this one is done, to people who may book (ADR 0039).
        var mayBook = User.HasClaim(UserClaimTypes.Permission, Permissions.BookingsManage);
        IReadOnlyList<RebookChoice> rebook = bill.Status == AppointmentStatus.Completed && mayBook
            ? await dispatcher.SendAsync(new RebookChoicesQuery(bill.ClientId, bill.ServiceId, bill.StaffMemberId, bill.Start.Date, bill.Start.Zone), cancellationToken)
            : [];
        return new CheckoutViewModel(bill, flash, error, await SellableAsync(bill, cancellationToken), rebook);
    }

    /// <summary>Where the product forms post; the BillProducts slice owns the routes.</summary>
    public static string ProductsPath(Guid appointmentId) => $"{BasePath}/{appointmentId}/products";

    public static string RemoveProductPath(Guid appointmentId, Guid lineId) => $"{BasePath}/{appointmentId}/products/{lineId}/remove";

    public static string ReturnProductPath(Guid appointmentId, Guid lineId) => $"{BasePath}/{appointmentId}/products/{lineId}/return";

    /// <summary>Products are rung up only on a bill that can be paid; for any other visit the list is not read.</summary>
    private async Task<IReadOnlyList<SellableProduct>> SellableAsync(VisitBill bill, CancellationToken cancellationToken)
        => bill.CanPay ? await dispatcher.SendAsync(new SellableProductsQuery(), cancellationToken) : [];

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">A refused request, already localized.</param>
/// <param name="Sellable">The products the desk can ring up; empty when the bill cannot be paid or the salon sells none.</param>
/// <param name="Rebook">One-tap choices for the client's next visit; empty until this one is completed.</param>
public sealed record CheckoutViewModel(VisitBill Bill, string? Flash, string? Error, IReadOnlyList<SellableProduct> Sellable, IReadOnlyList<RebookChoice> Rebook)
{
    /// <summary>The day list of the visit's day, for the way back; the Appointments module owns the route.</summary>
    public string DayPath => "/admin/appointments?date=" + LocalDatePattern.Iso.Format(Bill.Start.Date);

    /// <summary>Where the payment form posts: this bill.</summary>
    public string PayPath => CheckoutController.BasePath + "/" + Bill.AppointmentId;

    /// <summary>Where a gift voucher's code posts (ADR 0057); the RedeemVoucher slice owns the route.</summary>
    public string VoucherPath => CheckoutController.BasePath + "/" + Bill.AppointmentId + "/voucher";

    /// <summary>A voucher can go toward the visit while the client is here and the visit has something left to pay.</summary>
    public bool CanUseVoucher => Bill.CanPay && Bill.Outstanding.Amount > 0m;

    /// <summary>Where a part payment posts (ADR 0061); the PartPayment slice owns the route.</summary>
    public string PartPath => CheckoutController.BasePath + "/" + Bill.AppointmentId + "/part";

    /// <summary>A part can be paid another way while the client is here and the visit has something left to pay.</summary>
    public bool CanPayPart => Bill.CanPay && Bill.Outstanding.Amount > 0m;

    /// <summary>The id this page's part payment will have, so the same form sent twice is one part (ADR 0061).</summary>
    public Guid PartId { get; } = Guid.CreateVersion7();

    /// <summary>Why nothing is left to pay: it was paid, a voucher covered it, or the deposit did.</summary>
    public string SettledKey => Bill.Payment is not null ? "Payments.Checkout.Settled"
        : Bill.VoucherApplied is not null ? "Payments.Checkout.CoveredByVoucher"
        : "Payments.Checkout.CoveredByDeposit";

    /// <summary>The till of the day the visit was paid, or took place.</summary>
    public string TillPath => "/admin/payments/cash-up?date=" + LocalDatePattern.Iso.Format((Bill.Payment?.At ?? Bill.Start.ToInstant()).InZone(Bill.Start.Zone).Date);
}