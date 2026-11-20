using Microsoft.AspNetCore.Mvc;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Staff;
using Salon.Services.Localization;
using Salon.Services.Staff;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;

namespace Salon.Web.Features.Staff.SetPhoto;

/// <summary>
/// A staff member's profile photo (ADR 0088), for signed-in users with <see cref="Permissions.StaffManage"/>:
/// <c>POST /admin/staff/{id}/photo</c> takes the picture and <c>POST …/photo/remove</c> deletes it. The form itself
/// sits on the staff edit page (the <c>StaffPhotoForm</c> view component), so both answers go back there, with a
/// confirmation or with the reason the file was refused.
/// </summary>
[RequireTenant]
[RequirePermission(Permissions.StaffManage)]
[Route("admin/staff/{id:guid}/photo")]
public sealed class SetPhotoController(IDispatcher dispatcher, ILocalizationService localizer) : SalonControllerBase
{
    /// <summary>
    /// Far above the cap on purpose: a picture straight from a phone camera is several megabytes, and its owner
    /// should read why it was refused instead of meeting a bare "request too large" page.
    /// </summary>
    private const long RequestLimitBytes = 16 * 1024 * 1024;

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(RequestLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimitBytes)]
    public async Task<IActionResult> Upload(Guid id, IFormFile? photo, CancellationToken cancellationToken)
    {
        if (photo is not null && photo.Length > StaffPhotoRules.MaxBytes)
        {
            // Too large is known from the length alone; the file is not read into memory to be told so again.
            SetProblem(Refusal(StaffPhotoOutcome.TooLarge));
            return Redirect(EditPath(id));
        }

        var content = Array.Empty<byte>();
        if (photo is { Length: > 0 })
        {
            using var buffer = new MemoryStream((int)photo.Length);
            await photo.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var outcome = await dispatcher.SendAsync(new SetStaffPhotoCommand(id, content), cancellationToken);
        if (outcome == StaffPhotoOutcome.NoSuchPerson)
        {
            return NotFound();
        }

        if (outcome == StaffPhotoOutcome.Saved)
        {
            SetFlash(localizer["Staff.Photo.Saved"]);
        }
        else
        {
            SetProblem(Refusal(outcome));
        }

        return Redirect(EditPath(id));
    }

    [HttpPost("remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid id, CancellationToken cancellationToken)
    {
        if (!await dispatcher.SendAsync(new RemoveStaffPhotoCommand(id), cancellationToken))
        {
            return NotFound();
        }

        SetFlash(localizer["Staff.Photo.Removed"]);
        return Redirect(EditPath(id));
    }

    /// <summary>The staff edit page, where the photo form is shown; the EditStaff slice owns the route.</summary>
    public static string EditPath(Guid id) => $"/admin/staff/{id}/edit";

    private string Refusal(StaffPhotoOutcome outcome) => outcome switch
    {
        StaffPhotoOutcome.TooLarge => localizer.Format("Staff.Photo.Problem.TooLarge", StaffPhotoRules.MaxKilobytes),
        StaffPhotoOutcome.NotAPicture => localizer["Staff.Photo.Problem.NotAPicture"],
        _ => localizer["Staff.Photo.Problem.Empty"],
    };
}