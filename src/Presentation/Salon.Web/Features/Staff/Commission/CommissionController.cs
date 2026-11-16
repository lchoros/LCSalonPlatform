using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Core.Tenancy;
using Salon.Data;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.Commission;

/// <summary>The active staff with their commission rates, in the rota's order (ADR 0059).</summary>
public sealed record CommissionQuery : IQuery<IReadOnlyList<CommissionRate>>;

/// <summary>One person's rates as the form shows and posts them; empty means "not paid this way".</summary>
/// <param name="Name">Shown only; never read from the post.</param>
public sealed record CommissionRate(Guid StaffId, decimal? Services, decimal? Products, string? Name = null);

/// <summary>The whole table as posted (ADR 0059). A command: every row commits together or none does.</summary>
public sealed record SaveCommissionCommand(IReadOnlyList<CommissionRate>? Rates) : ICommand<int>;

public sealed class SaveCommissionValidator : AbstractValidator<SaveCommissionCommand>
{
    public SaveCommissionValidator(ILocalizationService localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        RuleForEach(c => c.Rates).ChildRules(rate =>
        {
            rate.RuleFor(r => r.Services)
                .Must(CommissionRules.IsRate)
                .WithMessage(_ => localizer["Staff.Commission.Validation.Rate"]);
            rate.RuleFor(r => r.Products)
                .Must(CommissionRules.IsRate)
                .WithMessage(_ => localizer["Staff.Commission.Validation.Rate"]);
        });
    }
}

public sealed class CommissionHandler(SalonDbContext db) : IRequestHandler<CommissionQuery, IReadOnlyList<CommissionRate>>
{
    public async Task<IReadOnlyList<CommissionRate>> HandleAsync(CommissionQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await db.StaffMembers.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.DisplayName).ThenBy(s => s.Id)
            .Select(s => new CommissionRate(s.Id, s.ServiceCommissionPercent, s.ProductCommissionPercent, s.DisplayName))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Writes the rates of the people posted, found through the tenant filter: an id of another salon, or of nobody, is
/// passed over. Returns how many people's rates changed.
/// </summary>
public sealed class SaveCommissionHandler(SalonDbContext db) : IRequestHandler<SaveCommissionCommand, int>
{
    public async Task<int> HandleAsync(SaveCommissionCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rates = (request.Rates ?? []).GroupBy(r => r.StaffId).ToDictionary(g => g.Key, g => g.Last());
        if (rates.Count == 0)
        {
            return 0;
        }

        var ids = rates.Keys.ToArray();
        var people = await db.StaffMembers.Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken);
        var changed = 0;
        foreach (var person in people)
        {
            var rate = rates[person.Id];
            if (person.ServiceCommissionPercent == rate.Services && person.ProductCommissionPercent == rate.Products)
            {
                continue;
            }

            person.ServiceCommissionPercent = rate.Services;
            person.ProductCommissionPercent = rate.Products;
            changed++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }
}

/// <summary>
/// Commission rates at <c>/admin/staff/commission</c> (ADR 0059): one table, a services and a products percent per
/// active person, both optional. What people are paid is the owner's to set, so the page needs
/// <see cref="Permissions.SettingsManage"/>, which only the owner has; a manager sees the figures in the report but
/// cannot change the rates, their own included.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.SettingsManage)]
[Route("admin/staff/commission")]
public sealed class CommissionController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>The report the rates are used in; the Reports module owns the route.</summary>
    public const string ReportPath = "/admin/reports";

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
        => View(new CommissionViewModel(await dispatcher.SendAsync(new CommissionQuery(), cancellationToken), TakeFlash()));

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SaveCommissionCommand form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);

        // Letters where a percent goes never reach the pipeline; say it next to the field, our way.
        if (!ModelState.IsValid)
        {
            var bad = ModelState.Where(e => e.Value?.Errors.Count > 0).Select(e => e.Key).ToArray();
            ModelState.Clear();
            foreach (var key in bad)
            {
                ModelState.AddModelError(key, localizer["Staff.Commission.Validation.Rate"]);
            }

            return View(await ShownAgainAsync(form, cancellationToken));
        }

        try
        {
            var changed = await dispatcher.SendAsync(form, cancellationToken);
            SetFlash(changed == 0 ? localizer["Staff.Commission.Unchanged"] : localizer["Staff.Commission.Saved"]);
            return Redirect("/admin/staff/commission");
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
        }

        return View(await ShownAgainAsync(form, cancellationToken));
    }

    /// <summary>The table again with what was typed, names from the database (never from the post).</summary>
    private async Task<CommissionViewModel> ShownAgainAsync(SaveCommissionCommand form, CancellationToken cancellationToken)
    {
        var posted = (form.Rates ?? []).GroupBy(r => r.StaffId).ToDictionary(g => g.Key, g => g.Last());
        var rows = await dispatcher.SendAsync(new CommissionQuery(), cancellationToken);
        return new CommissionViewModel(
            [.. rows.Select(r => posted.TryGetValue(r.StaffId, out var typed) ? typed with { Name = r.Name } : r)],
            null);
    }
}

public sealed record CommissionViewModel(IReadOnlyList<CommissionRate> Rates, string? Flash)
{
    public static string FieldName(int index, string field) => $"Rates[{index}].{field}";
}