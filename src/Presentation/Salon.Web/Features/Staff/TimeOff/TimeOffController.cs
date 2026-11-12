using FluentValidation;

using Microsoft.AspNetCore.Mvc;

using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Services.Localization;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.TimeOff;

/// <summary>
/// Time-off page at <c>/admin/staff/{id}/time-off</c> for signed-in users with <see cref="Permissions.StaffManage"/>
/// (ADR 0009, 0015): upcoming entries, past ones collapsed, an add form and a delete button per row. Every POST is
/// post-redirect-get back here with a confirmation; a rejected entry re-renders the page with each message next to its field
/// (an overlap as a form-level message). An id the salon does not own is a 404 on every verb.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff")]
public sealed class TimeOffController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    [HttpGet("{id:guid}/time-off")]
    public async Task<IActionResult> Index(Guid id, CancellationToken cancellationToken)
        => await RenderAsync(AddTimeOffCommand.Empty(id), TakeFlash(), cancellationToken);

    [HttpPost("{id:guid}/time-off")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Guid id, AddTimeOffCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        command = command with { StaffMemberId = id };

        try
        {
            if (!await dispatcher.SendAsync(command, cancellationToken))
            {
                return NotFound();
            }
        }
        catch (ValidationException ex)
        {
            ModelState.AddValidationFailures(ex.Errors);
            return await RenderAsync(command, null, cancellationToken);
        }

        SetFlash(localizer["Staff.TimeOff.Added"]);
        return Redirect(PagePath(id));
    }

    [HttpPost("{id:guid}/time-off/{entryId:guid}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, Guid entryId, CancellationToken cancellationToken)
    {
        if (!await dispatcher.SendAsync(new DeleteTimeOffCommand(id, entryId), cancellationToken))
        {
            return NotFound();
        }

        SetFlash(localizer["Staff.TimeOff.Deleted"]);
        return Redirect(PagePath(id));
    }

    private static string PagePath(Guid id) => $"/admin/staff/{id}/time-off";

    /// <summary>The name and the lists are not posted, so every render reads them again; a person removed meanwhile is a 404.</summary>
    private async Task<IActionResult> RenderAsync(AddTimeOffCommand form, string? flash, CancellationToken cancellationToken)
    {
        var page = await dispatcher.SendAsync(new GetTimeOffQuery(form.StaffMemberId), cancellationToken);
        return page is null ? NotFound() : View("Index", new TimeOffViewModel(page, form, flash));
    }
}

/// <param name="Page">The person and their entries.</param>
/// <param name="Form">The add form as posted (re-render) or empty (GET).</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
public sealed record TimeOffViewModel(StaffTimeOffPage Page, AddTimeOffCommand Form, string? Flash);