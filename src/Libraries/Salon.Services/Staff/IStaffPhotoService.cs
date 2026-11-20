using Salon.Core.Staff;

namespace Salon.Services.Staff;

/// <summary>
/// The faces of the salon's staff (ADR 0088). A service, not a slice, because the upload form, the address a browser
/// fetches, the staff list and the public "who with" picker all need it and none may reference another.
/// </summary>
public interface IStaffPhotoService
{
    /// <summary>
    /// Which staff members have a picture, and the version of each (see <see cref="StaffPhotoRules.VersionOf"/>).
    /// What a list asks: two small columns, never the bytes.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(CancellationToken cancellationToken = default);

    /// <summary>The version of one person's picture, or null when they have none (or the salon has no such person).</summary>
    Task<string?> VersionAsync(Guid staffMemberId, CancellationToken cancellationToken = default);

    /// <summary>The picture itself with whether the person is active; null when there is none in this salon.</summary>
    Task<StaffPhotoFile?> FindAsync(Guid staffMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the picture in place of whatever the person had. The content is judged by its bytes
    /// (<see cref="StaffPhotoRules.Check"/>); nothing is stored unless the outcome is <see cref="StaffPhotoOutcome.Saved"/>.
    /// Call it inside a transaction (a command): it holds the person's row so two uploads queue up.
    /// </summary>
    Task<StaffPhotoOutcome> SetAsync(Guid staffMemberId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default);

    /// <summary>Deletes the person's picture. False when the salon has no such person; true also when they had none.</summary>
    Task<bool> RemoveAsync(Guid staffMemberId, CancellationToken cancellationToken = default);
}

/// <param name="Version">Changes with every new picture; the address a browser may keep for good carries it.</param>
/// <param name="StaffIsActive">False for somebody who no longer works here: their face is not for visitors.</param>
public sealed record StaffPhotoFile(string ContentType, ReadOnlyMemory<byte> Content, string Version, bool StaffIsActive);

public enum StaffPhotoOutcome
{
    Saved,

    /// <summary>The salon has no such staff member.</summary>
    NoSuchPerson,

    /// <summary>No file, or one with nothing in it.</summary>
    Empty,

    /// <summary>More than <see cref="StaffPhotoRules.MaxBytes"/>.</summary>
    TooLarge,

    /// <summary>Not a JPEG, PNG or WebP picture.</summary>
    NotAPicture,
}