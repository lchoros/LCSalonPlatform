using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Appointments;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Appointments;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.RedeemVoucher;

/// <summary>
/// Uses a gift voucher toward a visit (ADR 0057). A command: the claim on the voucher, the line on the client's
/// account and, when nothing is left to pay, the end of the visit commit together.
/// </summary>
public sealed record RedeemVoucherCommand(Guid AppointmentId, string? Code, Guid? ByUserId) : ICommand<VoucherApplied>;

/// <summary>
/// Hands the voucher to <see cref="IGiftVoucherService"/>. When the voucher covers everything that was left and the
/// client is at the chair, the visit is completed, as paying would have done: no second tap on "Complete".
/// </summary>
public sealed class RedeemVoucherHandler(IGiftVoucherService vouchers, IBookingService booking) : IRequestHandler<RedeemVoucherCommand, VoucherApplied>
{
    public async Task<VoucherApplied> HandleAsync(RedeemVoucherCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var applied = await vouchers.ApplyAsync(request.AppointmentId, request.Code, request.ByUserId, cancellationToken);
        if (applied.Bill.Status != AppointmentStatus.Arrived || applied.Bill.HasSomethingToPay)
        {
            return applied;
        }

        await booking.CompleteAsync(request.AppointmentId, request.ByUserId, cancellationToken);
        return applied with { Bill = applied.Bill with { Status = AppointmentStatus.Completed } };
    }
}

/// <summary>
/// <c>POST /admin/payments/checkout/{appointmentId}/voucher</c> (ADR 0057): a gift voucher's code typed at the
/// checkout. Tenant only, behind the checkout's own permissions (<see cref="Permissions.PaymentsRecord"/> and
/// <see cref="Permissions.ClientsView"/>). Every answer is a redirect to the bill, with what went onto it and what is
/// left on the voucher, or with the reason nothing changed. A wrong code and another salon's code read the same.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/checkout/{appointmentId:guid}/voucher")]
public sealed class RedeemVoucherController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The bill of a visit; the Checkout slice owns the route.</summary>
    public const string CheckoutPath = "/admin/payments/checkout";

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redeem(Guid appointmentId, string? code, CancellationToken cancellationToken)
    {
        try
        {
            var applied = await dispatcher.SendAsync(new RedeemVoucherCommand(appointmentId, code, CurrentUserId()), cancellationToken);
            var culture = CultureInfo.CurrentCulture;
            SetFlash(localizer.Format("Payments.Vouchers.Applied", applied.Amount.ToDisplayString(culture), applied.Code, applied.Left.ToDisplayString(culture)));
        }
        catch (GiftVoucherException ex)
        {
            SetProblem(localizer[$"Payments.Vouchers.Refused.{ex.Refusal}"]);
        }

        return Redirect($"{CheckoutPath}/{appointmentId}");
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}