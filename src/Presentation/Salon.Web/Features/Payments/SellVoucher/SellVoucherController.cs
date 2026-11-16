using System.Globalization;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Formatting;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.SellVoucher;

/// <summary>The day a voucher sold today would expire unless the desk says otherwise (ADR 0057).</summary>
public sealed record DefaultVoucherExpiryQuery : IQuery<LocalDate?>;

/// <summary>
/// The sale form as posted (ADR 0057): how much, how it was paid, the last day it can be used and who it is for.
/// <see cref="SellVoucherValidator"/> holds the field rules. A command: the line of money and the voucher commit
/// together.
/// </summary>
/// <param name="Amount">What the voucher is worth, which is what the buyer pays.</param>
/// <param name="ExpiresOn">ISO date; empty for a voucher that never expires.</param>
/// <param name="SaleId">The id the form was given; the same form sent twice is one voucher.</param>
public sealed record SellVoucherCommand(decimal? Amount, PaymentMethod? Method, string? ExpiresOn, string? Note, Guid? SaleId = null, Guid? ByUserId = null) : ICommand<VoucherSummary>
{
    /// <summary>An ISO date as typed; null for an empty field. An unreadable one is the validator's to refuse.</summary>
    public static LocalDate? Day(string? text)
        => !string.IsNullOrWhiteSpace(text) && LocalDatePattern.Iso.Parse(text.Trim()) is { Success: true } parsed ? parsed.Value : null;
}

public sealed class SellVoucherValidator : AbstractValidator<SellVoucherCommand>
{
    public SellVoucherValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        RuleFor(c => c.Amount)
            .Must(amount => amount is { } value && GiftVoucherRules.IsAmount(value))
            .WithMessage(_ => localizer.Format("Payments.Vouchers.Validation.Amount", GiftVoucherRules.MaxAmount));

        RuleFor(c => c.Method)
            .Must(method => method is { } value && GiftVoucherRules.PaymentMethods.Contains(value))
            .WithMessage(_ => localizer["Payments.Vouchers.Validation.Method"]);

        RuleFor(c => c.ExpiresOn)
            .Must(text => string.IsNullOrWhiteSpace(text) || SellVoucherCommand.Day(text) is not null)
            .WithMessage(_ => localizer["Payments.Vouchers.Validation.Expiry"]);

        RuleFor(c => c.Note)
            .Must(GiftVoucherRules.NoteFits)
            .WithMessage(_ => localizer.Format("Payments.Vouchers.Validation.Note", GiftVoucher.NoteMaxLength));
    }
}

public sealed class DefaultVoucherExpiryHandler(IGiftVoucherService vouchers) : IRequestHandler<DefaultVoucherExpiryQuery, LocalDate?>
{
    public Task<LocalDate?> HandleAsync(DefaultVoucherExpiryQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return vouchers.DefaultExpiryAsync(cancellationToken);
    }
}

/// <summary>Hands the sale to <see cref="IGiftVoucherService"/>, which owns the voucher; nothing about money is decided in a slice.</summary>
public sealed class SellVoucherHandler(IGiftVoucherService vouchers) : IRequestHandler<SellVoucherCommand, VoucherSummary>
{
    public Task<VoucherSummary> HandleAsync(SellVoucherCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The validator has run in the pipeline; these only narrow the types.
        var amount = request.Amount ?? throw new ArgumentException("A voucher needs its amount.", nameof(request));
        var method = request.Method ?? throw new ArgumentException("A sale needs its method.", nameof(request));
        return vouchers.SellAsync(amount, method, SellVoucherCommand.Day(request.ExpiresOn), request.Note, request.ByUserId, request.SaleId, cancellationToken);
    }
}

/// <summary>
/// The sale of a gift voucher at <c>/admin/payments/vouchers/new</c> (ADR 0057). Tenant only, for whoever takes
/// payments (<see cref="Permissions.PaymentsRecord"/>). A valid post lands on the new voucher's page, where the code
/// is; an invalid one shows the form again with the reason next to its field.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[Route("admin/payments/vouchers/new")]
public sealed class SellVoucherController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The list of vouchers and a voucher's own page; the Vouchers slice owns the routes.</summary>
    public const string ListPath = "/admin/payments/vouchers";

    [HttpGet("")]
    public async Task<IActionResult> Form(CancellationToken cancellationToken)
    {
        var expiry = await dispatcher.SendAsync(new DefaultVoucherExpiryQuery(), cancellationToken);
        return View(new SellVoucherViewModel(new SellVoucherCommand(null, PaymentMethod.Cash, expiry is { } day ? LocalDatePattern.Iso.Format(day) : null, null)));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SellVoucherCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        // Letters where the amount goes never reach the pipeline; say it our way.
        if (!ModelState.IsValid)
        {
            ModelState.Clear();
            ModelState.AddModelError(nameof(SellVoucherCommand.Amount), localizer.Format("Payments.Vouchers.Validation.Amount", GiftVoucherRules.MaxAmount));
            return View(new SellVoucherViewModel(form));
        }

        try
        {
            var voucher = await dispatcher.SendAsync(form with { ByUserId = CurrentUserId() }, cancellationToken);
            SetFlash(localizer.Format("Payments.Vouchers.Sold", voucher.Initial.ToDisplayString(CultureInfo.CurrentCulture), voucher.Code));
            return Redirect($"{ListPath}/{voucher.Id}");
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
        }
        catch (GiftVoucherException ex)
        {
            ModelState.AddModelError(string.Empty, localizer[$"Payments.Vouchers.Refused.{ex.Refusal}"]);
        }

        return View(new SellVoucherViewModel(form));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

public sealed record SellVoucherViewModel(SellVoucherCommand Form)
{
    /// <summary>
    /// The id this form's sale will have (ADR 0057). Kept across a refused post, so the same form recorded twice, by a
    /// second click or a reload, is one voucher.
    /// </summary>
    public Guid SaleId { get; } = Form.SaleId ?? Guid.CreateVersion7();
}