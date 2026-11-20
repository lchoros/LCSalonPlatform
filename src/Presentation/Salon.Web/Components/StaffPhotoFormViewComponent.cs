using Microsoft.AspNetCore.Mvc;
using Salon.Core.Staff;
using Salon.Services.Staff;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Ui;

namespace Salon.Web.Components;

/// <summary>
/// The profile photo of a staff member on the staff edit page (ADR 0088): the face as clients see it, a form to
/// upload a picture and, when there is one, a button to remove it. A view component, not a slice: the page belongs
/// to the EditStaff slice and the two posts to the SetPhoto slice, and neither may reference the other. It also
/// shows what the last post left behind, the confirmation or the reason a file was refused.
/// </summary>
public sealed class StaffPhotoFormViewComponent(IStaffPhotoService photos) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(Guid staffMemberId, string? name)
    {
        var version = await photos.VersionAsync(staffMemberId, HttpContext.RequestAborted);
        return View(new StaffPhotoFormModel(
            name ?? string.Empty,
            StaffPhotoAddress.For(staffMemberId, version),
            $"/admin/staff/{staffMemberId}/photo",
            string.Join(',', StaffPhotoRules.ContentTypes),
            StaffPhotoRules.MaxKilobytes,
            TempData[SalonControllerBase.FlashTempDataKey] as string,
            TempData[SalonControllerBase.ProblemTempDataKey] as string));
    }
}

/// <param name="Name">The person's name: the letter shown while there is no picture comes from it.</param>
/// <param name="PhotoPath">The address of the current picture; null when there is none.</param>
/// <param name="UploadPath">Where the picture is posted; removing posts to <see cref="RemovePath"/>. The SetPhoto slice owns both.</param>
/// <param name="Accept">The kinds of file the picker offers; the server judges the bytes whatever was picked.</param>
/// <param name="Flash">Confirmation left by the previous request (post-redirect-get), if any.</param>
/// <param name="Problem">Why the last file was refused, if it was.</param>
public sealed record StaffPhotoFormModel(string Name, string? PhotoPath, string UploadPath, string Accept, int MaxKilobytes, string? Flash, string? Problem)
{
    public string RemovePath => UploadPath + "/remove";

    public bool HasPhoto => PhotoPath is not null;
}