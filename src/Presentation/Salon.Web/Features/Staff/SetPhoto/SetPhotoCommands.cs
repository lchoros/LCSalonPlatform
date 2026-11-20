using Salon.Core.Cqrs;
using Salon.Services.Staff;

namespace Salon.Web.Features.Staff.SetPhoto;

/// <summary>
/// Puts a picture in place of whatever the staff member had (ADR 0088). Only the bytes travel: the file's name and
/// the type the browser claimed are left at the door, and the service judges the content by its first bytes.
/// </summary>
/// <param name="StaffMemberId">Whose face it is; a person of another salon is not found.</param>
/// <param name="Content">The file as uploaded; empty when none was chosen.</param>
public sealed record SetStaffPhotoCommand(Guid StaffMemberId, ReadOnlyMemory<byte> Content) : ICommand<StaffPhotoOutcome>;

/// <summary>Deletes the staff member's picture (ADR 0088). False when the salon has no such person.</summary>
public sealed record RemoveStaffPhotoCommand(Guid StaffMemberId) : ICommand<bool>;

/// <summary>Runs inside the pipeline's transaction (ADR 0003), which is what makes two uploads for one person queue up.</summary>
public sealed class SetStaffPhotoHandler(IStaffPhotoService photos) : IRequestHandler<SetStaffPhotoCommand, StaffPhotoOutcome>
{
    public Task<StaffPhotoOutcome> HandleAsync(SetStaffPhotoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return photos.SetAsync(request.StaffMemberId, request.Content, cancellationToken);
    }
}

public sealed class RemoveStaffPhotoHandler(IStaffPhotoService photos) : IRequestHandler<RemoveStaffPhotoCommand, bool>
{
    public Task<bool> HandleAsync(RemoveStaffPhotoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return photos.RemoveAsync(request.StaffMemberId, cancellationToken);
    }
}