using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.ReturnCounterSale;

/// <summary>The sales taken at the counter on one day (ADR 0054), newest first. A query: no transaction, tenant required.</summary>
public sealed record CounterSalesOfDayQuery(LocalDate? Day) : IQuery<CounterSaleDay>;

/// <summary>
/// Gives the money back for units of one line of a counter sale (ADR 0054). A command: runs in the pipeline's
/// transaction, so the claim on the units, the money line and the shelf commit together.
/// </summary>
public sealed record ReturnCounterSaleCommand(Guid SaleId, Guid LineId, int Quantity, PaymentChoice Method, bool BackOnShelf, Guid? ByUserId) : ICommand<Money>;

public sealed class CounterSalesOfDayHandler(ICounterSaleService sales) : IRequestHandler<CounterSalesOfDayQuery, CounterSaleDay>
{
    public Task<CounterSaleDay> HandleAsync(CounterSalesOfDayQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return sales.ListAsync(request.Day, cancellationToken);
    }
}

/// <summary>Hands the return to <see cref="ICounterSaleService"/>, which owns the sale; nothing about money is decided in a slice.</summary>
public sealed class ReturnCounterSaleHandler(ICounterSaleService sales) : IRequestHandler<ReturnCounterSaleCommand, Money>
{
    public Task<Money> HandleAsync(ReturnCounterSaleCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return sales.ReturnAsync(request.SaleId, request.LineId, request.Quantity, request.Method, request.BackOnShelf, request.ByUserId, cancellationToken);
    }
}

/// <summary>
/// The counter sales of a day at <c>/admin/payments/sales?date=</c> and the return of a product from one of them at
/// <c>POST /admin/payments/sales/{saleId}/lines/{lineId}/return</c> (ADR 0054). Tenant only, for whoever takes
/// payments (<see cref="Permissions.PaymentsRecord"/>), like the sale itself. A counter sale has no client and no
/// visit, so the day it was taken is how the desk finds it again. Every answer to a post is a redirect back to the
/// day, with a confirmation or with the reason nothing changed; another salon's id is a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[Route("admin/payments/sales")]
public sealed class ReturnCounterSaleController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/payments/sales";

    /// <summary>A new sale at the counter; the CounterSale slice owns the route.</summary>
    public const string NewSalePath = "/admin/payments/sale";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? date, CancellationToken cancellationToken)
    {
        var day = await dispatcher.SendAsync(new CounterSalesOfDayQuery(ParseDate(date)), cancellationToken);
        return View(new ReturnCounterSaleViewModel(day, TakeProblem(), TakeFlash()));
    }

    [HttpPost("{saleId:guid}/lines/{lineId:guid}/return")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Return(Guid saleId, Guid lineId, int? quantity, PaymentChoice? method, bool backOnShelf, string? date, CancellationToken cancellationToken)
    {
        try
        {
            var back = await dispatcher.SendAsync(
                new ReturnCounterSaleCommand(saleId, lineId, quantity ?? 0, method ?? PaymentMethod.Cash, backOnShelf, CurrentUserId()),
                cancellationToken);
            SetFlash(localizer.Format("Payments.CounterSale.Returns.Recorded", back.ToDisplayString(CultureInfo.CurrentCulture)));
        }
        catch (CounterSaleException ex) when (ex.Refusal == CounterSaleRefusal.SaleNotFound)
        {
            return NotFound();
        }
        catch (CounterSaleException ex)
        {
            SetProblem(localizer[$"Payments.CounterSale.Refused.{ex.Refusal}"]);
        }

        return Redirect(ParseDate(date) is { } day ? DayPath(day) : Path);
    }

    public static string DayPath(LocalDate day) => Path + "?date=" + LocalDatePattern.Iso.Format(day);

    public static string ReturnPath(Guid saleId, Guid lineId) => $"{Path}/{saleId}/lines/{lineId}/return";

    private static LocalDate? ParseDate(string? date)
        => date is { Length: > 0 } && LocalDatePattern.Iso.Parse(date.Trim()) is { Success: true } parsed ? parsed.Value : null;

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Problem">Why the last return was not recorded, already localized.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record ReturnCounterSaleViewModel(CounterSaleDay Day, string? Problem, string? Flash)
{
    public bool IsToday => Day.Day == Day.Today;

    public bool HasSales => Day.Sales.Count > 0;
}