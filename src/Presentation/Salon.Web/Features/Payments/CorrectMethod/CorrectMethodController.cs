using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using NodaTime;
using NodaTime.Text;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Payments;
using Salon.Services.Localization;
using Salon.Services.Payments;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Payments.CorrectMethod;

/// <summary>One line of money as the correction page shows it (ADR 0064). A query: no transaction, tenant required.</summary>
public sealed record CorrectableLineQuery(Guid EntryId) : IQuery<CorrectableLine?>;

/// <summary>
/// Moves a line of money to the method it was really paid by (ADR 0064). A command: both lines of the correction
/// commit or neither does.
/// </summary>
/// <param name="CorrectionId">The id the form was given; the same form sent twice is one correction.</param>
public sealed record CorrectMethodCommand(Guid EntryId, PaymentChoice Method, Guid? CorrectionId, Guid? ByUserId) : ICommand<MethodCorrectionResult>;

public sealed class CorrectableLineHandler(IMethodCorrectionService corrections) : IRequestHandler<CorrectableLineQuery, CorrectableLine?>
{
    public Task<CorrectableLine?> HandleAsync(CorrectableLineQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return corrections.GetAsync(request.EntryId, cancellationToken);
    }
}

/// <summary>Hands the correction to <see cref="IMethodCorrectionService"/>; nothing about money is decided in a slice.</summary>
public sealed class CorrectMethodHandler(IMethodCorrectionService corrections) : IRequestHandler<CorrectMethodCommand, MethodCorrectionResult>
{
    public Task<MethodCorrectionResult> HandleAsync(CorrectMethodCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return corrections.CorrectAsync(request.EntryId, request.Method, request.ByUserId, request.CorrectionId, cancellationToken);
    }
}

/// <summary>
/// <c>/admin/payments/corrections/{entryId}</c> (ADR 0064): "I pressed cash, it was card". GET shows one line of
/// today's till with the method it stands at and the methods it can be moved to; POST moves it and goes back to the
/// till with what was done. Tenant only, behind the till's own permissions (<see cref="Permissions.PaymentsRecord"/>
/// and <see cref="Permissions.ClientsView"/>: the page names the client). A refused request shows the page again
/// with the reason; another salon's line is a 404 like a missing one.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.PaymentsRecord)]
[RequirePermission(Permissions.ClientsView)]
[Route("admin/payments/corrections/{entryId:guid}")]
public sealed class CorrectMethodController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    public const string BasePath = "/admin/payments/corrections";

    /// <summary>The page of one line and the address its form posts to (ADR 0043: addresses are built here, not in the view).</summary>
    public static string LinePath(Guid entryId) => BasePath + "/" + entryId;

    /// <summary>The till of one day, for the way back; the CashUp slice owns the route.</summary>
    public static string TillPath(LocalDate day) => "/admin/payments/cash-up?date=" + LocalDatePattern.Iso.Format(day);

    [HttpGet("")]
    public async Task<IActionResult> Show(Guid entryId, CancellationToken cancellationToken)
    {
        var line = await dispatcher.SendAsync(new CorrectableLineQuery(entryId), cancellationToken);
        return line is null ? NotFound() : View(new CorrectMethodViewModel(line, null));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Correct(Guid entryId, PaymentChoice? method, Guid? correction, CancellationToken cancellationToken)
    {
        string error;

        // No method, or one that did not even bind: said our way, and nothing is recorded.
        if (!ModelState.IsValid || method is not { } chosen)
        {
            error = localizer["Payments.Correct.Refused.Incomplete"];
        }
        else
        {
            var result = await dispatcher.SendAsync(new CorrectMethodCommand(entryId, chosen, correction, CurrentUserId()), cancellationToken);
            if (result is { Outcome: MethodCorrectionOutcome.Corrected, Day: { } day, Now: { } now })
            {
                SetFlash(localizer.Format("Payments.Correct.Done", PaymentMethodNames.Name(localizer, now.Method, now.MethodName)));
                return Redirect(TillPath(day));
            }

            if (result.Outcome is MethodCorrectionOutcome.NotFound or MethodCorrectionOutcome.NotCorrectable)
            {
                return NotFound();
            }

            error = localizer[$"Payments.Correct.Refused.{result.Outcome}"];
        }

        var line = await dispatcher.SendAsync(new CorrectableLineQuery(entryId), cancellationToken);
        return line is null ? NotFound() : View("Show", new CorrectMethodViewModel(line, error));
    }

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(UserClaimTypes.Subject), out var id) ? id : null;
}

/// <param name="Error">A refused request, already localized.</param>
public sealed record CorrectMethodViewModel(CorrectableLine Line, string? Error)
{
    /// <summary>The id the form carries: the same form sent twice is one correction.</summary>
    public Guid CorrectionId { get; } = Guid.CreateVersion7();
}