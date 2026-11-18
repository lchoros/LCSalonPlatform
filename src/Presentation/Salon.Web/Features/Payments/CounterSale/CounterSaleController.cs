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

namespace Salon.Web.Features.Payments.CounterSale;

/// <summary>
/// A sale at the counter without a visit at <c>/admin/payments/sale</c> (ADR 0053): somebody walks in, buys a bottle
/// and leaves. Tenant only, for whoever takes payments (<see cref="Permissions.PaymentsRecord"/>). Two steps without a
/// script: choose the products and see the total (a GET, so nothing is recorded by looking), then record it (a POST).
/// A recorded sale lands back on the empty form with a confirmation.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[Route("admin/payments/sale")]
public sealed class CounterSaleController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/payments/sale";

    /// <summary>The till of today; the CashUp slice owns the route.</summary>
    public const string TillPath = "/admin/payments/cash-up";

    /// <summary>The sales already taken at the counter, where a product is returned; the ReturnCounterSale slice owns the route.</summary>
    public const string SalesPath = "/admin/payments/sales";

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] List<CounterSaleRow>? rows, CancellationToken cancellationToken)
    {
        var form = await dispatcher.SendAsync(new CounterSaleFormQuery(rows ?? []), cancellationToken);
        return View(new CounterSaleViewModel(form, form.Refusal is { } refusal ? Reason(refusal) : TakeProblem(), TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Record([FromForm] List<CounterSaleRow>? rows, PaymentChoice? method, Guid? sale, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await dispatcher.SendAsync(new RecordCounterSaleCommand(rows ?? [], method ?? PaymentMethod.Cash, CurrentUserId(), sale), cancellationToken);
            SetFlash(localizer.Format(
                "Payments.CounterSale.Recorded",
                receipt.Total.ToDisplayString(CultureInfo.CurrentCulture),
                PaymentMethodNames.Name(localizer, receipt.Method, receipt.MethodName)));
        }
        catch (CounterSaleException ex)
        {
            SetProblem(Reason(ex.Refusal));
        }

        return Redirect(Path);
    }

    private string Reason(CounterSaleRefusal refusal) => refusal switch
    {
        CounterSaleRefusal.QuantityOutOfRange => localizer.Format("Payments.CounterSale.Refused.QuantityOutOfRange", ProductSaleRules.MaxQuantity),
        _ => localizer[$"Payments.CounterSale.Refused.{refusal}"],
    };

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Problem">Why the chosen rows cannot be sold, or why the last sale was not recorded; already localized.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record CounterSaleViewModel(CounterSaleForm Form, string? Problem, string? Flash)
{
    /// <summary>
    /// The id this form's sale will have (ADR 0053). Sent back with the post, so the same form recorded twice, by a
    /// second click or a reload, is one sale.
    /// </summary>
    public Guid SaleId { get; } = Guid.CreateVersion7();
}