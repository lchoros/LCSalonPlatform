using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.Receipt;

/// <summary>The receipt of a paid visit (ADR 0070); null when the salon has no such visit or it is not paid. A query.</summary>
public sealed record ReceiptQuery(Guid AppointmentId) : IQuery<VisitReceipt?>;

/// <summary>Emails the receipt to the client. Nothing is stored; the outcome says why it was not sent.</summary>
public sealed record EmailReceiptCommand(Guid AppointmentId) : ICommand<ReceiptEmailOutcome>;

public sealed class ReceiptHandler(IReceiptService receipts) : IRequestHandler<ReceiptQuery, VisitReceipt?>
{
    public Task<VisitReceipt?> HandleAsync(ReceiptQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return receipts.GetAsync(request.AppointmentId, cancellationToken);
    }
}

public sealed class EmailReceiptHandler(IReceiptService receipts) : IRequestHandler<EmailReceiptCommand, ReceiptEmailOutcome>
{
    public Task<ReceiptEmailOutcome> HandleAsync(EmailReceiptCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return receipts.EmailAsync(request.AppointmentId, cancellationToken);
    }
}

/// <param name="Problem">Why the email did not go, already in words.</param>
public sealed record ReceiptViewModel(VisitReceipt Receipt, string? Flash, string? Problem)
{
    public string CheckoutPath => ReceiptController.CheckoutPath(Receipt.AppointmentId);

    public string EmailPath => ReceiptController.EmailPath(Receipt.AppointmentId);
}

/// <summary>
/// <c>/admin/payments/receipt/{appointmentId}</c> (ADR 0070): the receipt of a paid visit, to print from the browser
/// or to email to the client. Behind the checkout's own permissions (<see cref="Permissions.PaymentsRecord"/> and
/// <see cref="Permissions.ClientsView"/>). Another salon's visit, a missing one and one not paid yet are all a 404.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/receipt/{appointmentId:guid}")]
public sealed class ReceiptController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public static string Path(Guid appointmentId) => $"/admin/payments/receipt/{appointmentId}";

    public static string EmailPath(Guid appointmentId) => $"{Path(appointmentId)}/email";

    /// <summary>The visit's bill; the Checkout slice owns the route.</summary>
    public static string CheckoutPath(Guid appointmentId) => $"/admin/payments/checkout/{appointmentId}";

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid appointmentId, CancellationToken cancellationToken)
    {
        var receipt = await dispatcher.SendAsync(new ReceiptQuery(appointmentId), cancellationToken);
        return receipt is null ? NotFound() : View(new ReceiptViewModel(receipt, TakeFlash(), TakeProblem()));
    }

    [HttpPost("email")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Email(Guid appointmentId, CancellationToken cancellationToken)
    {
        var outcome = await dispatcher.SendAsync(new EmailReceiptCommand(appointmentId), cancellationToken);
        switch (outcome)
        {
            case ReceiptEmailOutcome.NoReceipt:
                return NotFound();
            case ReceiptEmailOutcome.Sent:
                SetFlash(localizer["Payments.Receipt.Sent"]);
                break;
            default:
                SetProblem(localizer[$"Payments.Receipt.NotSent.{outcome}"]);
                break;
        }

        return Redirect(Path(appointmentId));
    }
}