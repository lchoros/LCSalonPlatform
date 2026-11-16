using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Settings.NextVisit;

/// <summary>
/// <c>/admin/settings/next-visit</c> (ADR 0039): how many weeks ahead the desk offers the next visit when a client
/// pays. Up to three choices, 0 leaves one out, behind <c>Settings.Manage</c>.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/settings/next-visit")]
public sealed class NextVisitController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var form = await dispatcher.SendAsync(new GetNextVisitQuery(), cancellationToken);
        return View(new NextVisitViewModel(form, TakeFlash()));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SaveNextVisitCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        // Letters in a number never reach the pipeline, and MVC's own words for it name the property. Say it our way.
        if (!ModelState.IsValid)
        {
            foreach (var (_, entry) in ModelState.Where(pair => pair.Value is { Errors.Count: > 0 }))
            {
                entry!.Errors.Clear();
                entry.Errors.Add(localizer["Settings.NextVisit.Validation.Required"]);
            }

            return View(new NextVisitViewModel(form, null));
        }

        try
        {
            await dispatcher.SendAsync(form, cancellationToken);
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return View(new NextVisitViewModel(form, null));
        }

        SetFlash(localizer["Settings.NextVisit.Saved"]);
        return RedirectToAction(nameof(Index));
    }
}

/// <param name="Form">The three fields, as saved or as just typed.</param>
public sealed record NextVisitViewModel(SaveNextVisitCommand Form, string? Flash)
{
    /// <summary>The choices the desk would see with the values in the form, shortest first.</summary>
    public IReadOnlyList<int> Offered { get; } = Form.Offered();

    /// <summary>The fields in the order they are shown: name, label key and value.</summary>
    public IReadOnlyList<(string Name, string LabelKey, int? Value)> Fields { get; } =
    [
        (nameof(SaveNextVisitCommand.FirstWeeks), "Settings.NextVisit.First", Form.FirstWeeks),
        (nameof(SaveNextVisitCommand.SecondWeeks), "Settings.NextVisit.Second", Form.SecondWeeks),
        (nameof(SaveNextVisitCommand.ThirdWeeks), "Settings.NextVisit.Third", Form.ThirdWeeks),
    ];
}