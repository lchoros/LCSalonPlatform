using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Domain;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Lists;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.Vouchers;

/// <summary>The salon's gift vouchers (ADR 0057), newest first. A query: no transaction, tenant required.</summary>
public sealed record VouchersQuery(bool IncludeSpent, string? Search, int? Page) : IQuery<VoucherList>;

/// <summary>One voucher with its story; null when the salon has none with this id.</summary>
public sealed record VoucherQuery(Guid Id) : IQuery<VoucherCard?>;

/// <summary>Gives the money back for a voucher nobody has used (ADR 0057). A command: the claim and the line of money commit together.</summary>
public sealed record RefundVoucherCommand(Guid Id, PaymentMethod Method, Guid? ByUserId) : ICommand<Money>;

public sealed class VouchersHandler(IGiftVoucherService vouchers) : IRequestHandler<VouchersQuery, VoucherList>
{
    public Task<VoucherList> HandleAsync(VouchersQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return vouchers.ListAsync(request.IncludeSpent, request.Search, request.Page, cancellationToken);
    }
}

public sealed class VoucherHandler(IGiftVoucherService vouchers) : IRequestHandler<VoucherQuery, VoucherCard?>
{
    public Task<VoucherCard?> HandleAsync(VoucherQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return vouchers.GetAsync(request.Id, cancellationToken);
    }
}

/// <summary>Hands the refund to <see cref="IGiftVoucherService"/>, which owns the voucher; nothing about money is decided in a slice.</summary>
public sealed class RefundVoucherHandler(IGiftVoucherService vouchers) : IRequestHandler<RefundVoucherCommand, Money>
{
    public Task<Money> HandleAsync(RefundVoucherCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return vouchers.RefundAsync(request.Id, request.Method, request.ByUserId, cancellationToken);
    }
}

/// <summary>
/// Gift vouchers at <c>/admin/payments/vouchers?show=&amp;q=&amp;page=</c> and one voucher at
/// <c>/admin/payments/vouchers/{id}</c> (ADR 0057). Tenant only, for whoever takes payments
/// (<see cref="Permissions.PaymentsRecord"/>): the desk sells them, looks them up by code and uses them. The list says
/// what the salon owes on vouchers that can still be used. <c>POST .../{id}/refund</c> gives the money back for one
/// nobody has used. Another salon's id is a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[Route("admin/payments/vouchers")]
public sealed class VouchersController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string Path = "/admin/payments/vouchers";
    public const string ShowParameter = "show";
    public const string ShowAll = "all";

    /// <summary>The form that sells a voucher; the SellVoucher slice owns the route.</summary>
    public const string SellPath = "/admin/payments/vouchers/new";

    /// <summary>The bill of a visit a voucher was used on; the Checkout slice owns the route.</summary>
    public const string CheckoutPath = "/admin/payments/checkout";

    [HttpGet("")]
    public async Task<IActionResult> Index(string? show, string? q, int? page, CancellationToken cancellationToken)
    {
        var all = string.Equals(show, ShowAll, StringComparison.OrdinalIgnoreCase);
        var list = await dispatcher.SendAsync(new VouchersQuery(all, q, page), cancellationToken);
        return View(new VouchersViewModel(
            list,
            new FilterTilesModel(localizer["Payments.Vouchers.FilterLabel"],
            [
                new("active", localizer.Format("Payments.Vouchers.Filter.Active", list.ActiveCount), ListAddress(false, list.Search), !list.IncludesSpent),
                new(ShowAll, localizer["Payments.Vouchers.Filter.All"], ListAddress(true, list.Search), list.IncludesSpent, 3),
            ]),
            new SearchBoxModel(Path, list.Search, localizer["Payments.Vouchers.Search"], [(ShowParameter, list.IncludesSpent ? ShowAll : null)], ListAddress(list.IncludesSpent, null)),
            PagerModel.For(list.Vouchers, number => ListAddress(list.IncludesSpent, list.Search, number)),
            TakeFlash()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Show(Guid id, CancellationToken cancellationToken)
    {
        var voucher = await dispatcher.SendAsync(new VoucherQuery(id), cancellationToken);
        return voucher is null ? NotFound() : View(new VoucherViewModel(voucher, TakeProblem(), TakeFlash()));
    }

    [HttpPost("{id:guid}/refund")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refund(Guid id, PaymentMethod? method, CancellationToken cancellationToken)
    {
        try
        {
            var back = await dispatcher.SendAsync(new RefundVoucherCommand(id, method ?? PaymentMethod.Cash, CurrentUserId()), cancellationToken);
            SetFlash(localizer.Format("Payments.Vouchers.Refunded", back.ToDisplayString(CultureInfo.CurrentCulture)));
        }
        catch (GiftVoucherException ex) when (ex.Refusal == GiftVoucherRefusal.NotFound)
        {
            return NotFound();
        }
        catch (GiftVoucherException ex)
        {
            SetProblem(localizer[$"Payments.Vouchers.Refused.{ex.Refusal}"]);
        }

        return Redirect(CardPath(id));
    }

    public static string CardPath(Guid id) => $"{Path}/{id}";

    public static string RefundPath(Guid id) => $"{Path}/{id}/refund";

    public static string VisitPath(Guid appointmentId) => $"{CheckoutPath}/{appointmentId}";

    private static string ListAddress(bool all, string? search, int page = 1)
        => ListUrl.Build(
            Path,
            (ShowParameter, all ? ShowAll : null),
            (ListUrl.SearchParameter, search),
            (ListUrl.PageParameter, page.ToString(CultureInfo.InvariantCulture)));

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record VouchersViewModel(VoucherList List, FilterTilesModel Filter, SearchBoxModel Search, PagerModel Pager, string? Flash)
{
    public bool HasAny => List.Vouchers.Total > 0;
}

/// <param name="Problem">Why the last change was refused, already localized.</param>
public sealed record VoucherViewModel(VoucherCard Card, string? Problem, string? Flash);