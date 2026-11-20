using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Salon.Core.Cqrs;
using Salon.Core.Identity;
using Salon.Core.Tenancy;
using Salon.Services.Staff;
using Salon.Web.Framework.Controllers;
using Salon.Web.Framework.Mvc;
using Salon.Web.Framework.Ui;

namespace Salon.Web.Features.Staff.ShowPhoto;

/// <summary>
/// <c>GET /staff-photo/{staffMemberId}?v={version}</c> on the salon's host (ADR 0088): a staff member's face, for
/// the booking page and the staff pages. Without a sign-in, because clients choose who to book with before they are
/// anybody to us — so a visitor is given the picture of somebody who works here now and nobody else's; a signed-in
/// user of the salon also sees people who no longer do. Another salon's id finds nothing (the tenant filter).
/// The bytes came from a user: they go out as the kind of picture their first bytes were found to be, with sniffing
/// switched off and a policy that lets the response do nothing but be an image.
/// </summary>
[RequireTenant]
public sealed class ShowPhotoController(IDispatcher dispatcher, ITenantContext tenantContext) : SalonControllerBase
{
    /// <summary>
    /// Opened on its own the response may load nothing and run nothing; the style allowance is for the page a browser
    /// builds around a bare image, which styles itself.
    /// </summary>
    private const string OnlyAnImage = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    [HttpGet(StaffPhotoAddress.Route)]
    public async Task<IActionResult> Show(Guid staffMemberId, [FromQuery(Name = StaffPhotoAddress.VersionParameter)] string? version, CancellationToken cancellationToken)
    {
        var photo = await dispatcher.SendAsync(new StaffPhotoQuery(staffMemberId), cancellationToken);
        var signedIn = IsSignedInHere();
        if (photo is null || (!photo.StaffIsActive && !signedIn))
        {
            return NotFound();
        }

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = OnlyAnImage;
        Response.Headers.CacheControl = StaffPhotoAddress.CacheControlFor(
            photo.StaffIsActive,
            string.Equals(version, photo.Version, StringComparison.Ordinal),
            User.Identity?.IsAuthenticated == true);
        return File(photo.Content.ToArray(), photo.ContentType, lastModified: null, entityTag: new EntityTagHeaderValue($"\"{photo.Version}\""));
    }

    /// <summary>A cookie of another salon does not count on this salon's host (the same test the permission filter makes).</summary>
    private bool IsSignedInHere()
        => User.Identity?.IsAuthenticated == true
            && Guid.TryParse(User.FindFirst(UserClaimTypes.TenantId)?.Value, out var claimedTenant)
            && claimedTenant == tenantContext.TenantId;
}

/// <summary>The staff member's picture with whether they are active; null when this salon has none for that id. A query: tenant required.</summary>
public sealed record StaffPhotoQuery(Guid StaffMemberId) : IQuery<StaffPhotoFile?>;

public sealed class StaffPhotoHandler(IStaffPhotoService photos) : IRequestHandler<StaffPhotoQuery, StaffPhotoFile?>
{
    public Task<StaffPhotoFile?> HandleAsync(StaffPhotoQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return photos.FindAsync(request.StaffMemberId, cancellationToken);
    }
}