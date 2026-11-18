using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.PaymentMethods;

/// <summary>The ways the salon takes money (ADR 0060): which built-in methods it has on, and its own.</summary>
public sealed record PaymentMethodsQuery : IQuery<PaymentMethodList>;

/// <summary>The built-in switches as posted. An unticked box is not posted, which is "off". Cash is not a switch.</summary>
public sealed record SaveBuiltInMethodsCommand(bool CardAtSalonTerminal, bool BankTransfer, bool Voucher) : ICommand<bool>;

/// <summary>A method of the salon's own, by the name the desk will read.</summary>
public sealed record AddPaymentMethodCommand(string? Name) : ICommand<PaymentMethodResult>;

public sealed record RenamePaymentMethodCommand(Guid Id, string? Name) : ICommand<PaymentMethodResult>;

/// <summary>Takes a method out of the lists; one never used is removed.</summary>
public sealed record RetirePaymentMethodCommand(Guid Id) : ICommand<PaymentMethodResult>;

public sealed record RestorePaymentMethodCommand(Guid Id) : ICommand<PaymentMethodResult>;

public sealed class PaymentMethodsHandler(IPaymentMethodService methods) :
    IRequestHandler<PaymentMethodsQuery, PaymentMethodList>,
    IRequestHandler<SaveBuiltInMethodsCommand, bool>,
    IRequestHandler<AddPaymentMethodCommand, PaymentMethodResult>,
    IRequestHandler<RenamePaymentMethodCommand, PaymentMethodResult>,
    IRequestHandler<RetirePaymentMethodCommand, PaymentMethodResult>,
    IRequestHandler<RestorePaymentMethodCommand, PaymentMethodResult>
{
    public Task<PaymentMethodList> HandleAsync(PaymentMethodsQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return methods.ListAsync(cancellationToken);
    }

    public async Task<bool> HandleAsync(SaveBuiltInMethodsCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await methods.SaveBuiltInAsync(
            new PaymentMethodSettings { CardAtSalonTerminal = request.CardAtSalonTerminal, BankTransfer = request.BankTransfer, Voucher = request.Voucher },
            cancellationToken);
        return true;
    }

    public Task<PaymentMethodResult> HandleAsync(AddPaymentMethodCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return methods.AddAsync(request.Name, cancellationToken);
    }

    public Task<PaymentMethodResult> HandleAsync(RenamePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return methods.RenameAsync(request.Id, request.Name, cancellationToken);
    }

    public Task<PaymentMethodResult> HandleAsync(RetirePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return methods.RetireAsync(request.Id, cancellationToken);
    }

    public Task<PaymentMethodResult> HandleAsync(RestorePaymentMethodCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return methods.RestoreAsync(request.Id, cancellationToken);
    }
}

/// <summary>
/// <c>/admin/settings/payment-methods</c> (ADR 0060): the owner chooses which built-in methods the salon takes and adds
/// its own (PayPal, a second card machine, a cheque). A method only names how money the salon took itself changed
/// hands: nothing is charged through it and no account or key is asked for. Behind <c>Settings.Manage</c>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/payment-methods")]
public sealed class PaymentMethodsController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The till, where the methods are used; the CashUp slice owns the route.</summary>
    public const string TillPath = "/admin/payments/cash-up";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new PaymentMethodsViewModel(await dispatcher.SendAsync(new PaymentMethodsQuery(), cancellationToken), TakeFlash(), TakeProblem()));

    [HttpPost("built-in")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BuiltIn(SaveBuiltInMethodsCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        await dispatcher.SendAsync(form, cancellationToken);
        SetFlash(localizer["Settings.PaymentMethods.Saved"]);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(string? name, CancellationToken cancellationToken)
        => Done(await dispatcher.SendAsync(new AddPaymentMethodCommand(name), cancellationToken), "Settings.PaymentMethods.Added");

    [HttpPost("{id:guid}/rename")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rename(Guid id, string? name, CancellationToken cancellationToken)
        => Done(await dispatcher.SendAsync(new RenamePaymentMethodCommand(id, name), cancellationToken), "Settings.PaymentMethods.Renamed");

    [HttpPost("{id:guid}/retire")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retire(Guid id, CancellationToken cancellationToken)
        => Done(await dispatcher.SendAsync(new RetirePaymentMethodCommand(id), cancellationToken), "Settings.PaymentMethods.Retired");

    [HttpPost("{id:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid id, CancellationToken cancellationToken)
        => Done(await dispatcher.SendAsync(new RestorePaymentMethodCommand(id), cancellationToken), "Settings.PaymentMethods.Restored");

    /// <summary>Back to the page with what happened: the method's name in a confirmation, or why it was refused.</summary>
    private IActionResult Done(PaymentMethodResult result, string savedKey)
    {
        switch (result.Outcome)
        {
            case PaymentMethodOutcome.Saved:
                SetFlash(localizer.Format(savedKey, result.Name));
                break;
            case PaymentMethodOutcome.NotFound:
                return NotFound();
            case PaymentMethodOutcome.NameInvalid:
                SetProblem(localizer.Format("Settings.PaymentMethods.Refused.NameInvalid", SalonPaymentMethod.NameMaxLength));
                break;
            case PaymentMethodOutcome.TooMany:
                SetProblem(localizer.Format("Settings.PaymentMethods.Refused.TooMany", SalonPaymentMethodRules.MaxMethods));
                break;
            default:
                SetProblem(localizer[$"Settings.PaymentMethods.Refused.{result.Outcome}"]);
                break;
        }

        return RedirectToAction(nameof(Index));
    }
}

public sealed record PaymentMethodsViewModel(PaymentMethodList Methods, string? Flash, string? Problem)
{
    public IReadOnlyList<OwnPaymentMethod> InUse { get; } = [.. Methods.Own.Where(m => m.IsActive)];

    public IReadOnlyList<OwnPaymentMethod> Retired { get; } = [.. Methods.Own.Where(m => !m.IsActive)];

    public bool CanAdd => InUse.Count < SalonPaymentMethodRules.MaxMethods;

    public static string RenamePath(Guid id) => $"/admin/settings/payment-methods/{id}/rename";

    public static string RetirePath(Guid id) => $"/admin/settings/payment-methods/{id}/retire";

    public static string RestorePath(Guid id) => $"/admin/settings/payment-methods/{id}/restore";
}