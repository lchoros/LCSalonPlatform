using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Salon.Core.Staff;
using Salon.Data;

namespace Salon.Services.Staff;

/// <summary>
/// Reads and writes through the tenant filter, so another salon's staff member is simply not there. The log gets ids
/// and sizes only: a file's name can say who took the picture or where, so it is never read, let alone written down.
/// </summary>
internal sealed class StaffPhotoService(SalonDbContext db, IClock clock, ILogger<StaffPhotoService> logger) : IStaffPhotoService
{
    public async Task<IReadOnlyDictionary<Guid, string>> VersionsAsync(CancellationToken cancellationToken = default)
    {
        var stamps = await db.StaffPhotos.AsNoTracking()
            .Select(p => new { p.StaffMemberId, p.UpdatedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return stamps.ToDictionary(p => p.StaffMemberId, p => StaffPhotoRules.VersionOf(p.UpdatedAt));
    }

    public async Task<string?> VersionAsync(Guid staffMemberId, CancellationToken cancellationToken = default)
    {
        var stamp = await db.StaffPhotos.AsNoTracking()
            .Where(p => p.StaffMemberId == staffMemberId)
            .Select(p => (Instant?)p.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return stamp is { } updatedAt ? StaffPhotoRules.VersionOf(updatedAt) : null;
    }

    public async Task<StaffPhotoFile?> FindAsync(Guid staffMemberId, CancellationToken cancellationToken = default)
    {
        var photo = await db.StaffPhotos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.StaffMemberId == staffMemberId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            return null;
        }

        var active = await db.StaffMembers.AsNoTracking()
            .Where(s => s.Id == staffMemberId)
            .Select(s => (bool?)s.IsActive)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return active is { } isActive
            ? new StaffPhotoFile(photo.ContentType, photo.Bytes, StaffPhotoRules.VersionOf(photo.UpdatedAt), isActive)
            : null;
    }

    public async Task<StaffPhotoOutcome> SetAsync(Guid staffMemberId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        switch (StaffPhotoRules.Check(content.Span))
        {
            case StaffPhotoProblem.Empty:
                return StaffPhotoOutcome.Empty;
            case StaffPhotoProblem.TooLarge:
                return StaffPhotoOutcome.TooLarge;
            case StaffPhotoProblem.NotAPicture:
                return StaffPhotoOutcome.NotAPicture;
        }

        // Holds the person's row until the caller's transaction ends, so two uploads for one person queue up instead
        // of both finding no picture and both adding one (the unique index would refuse the second with an error).
        if (await LockPersonAsync(staffMemberId, cancellationToken).ConfigureAwait(false) == 0)
        {
            return StaffPhotoOutcome.NoSuchPerson;
        }

        var contentType = StaffPhotoRules.SniffContentType(content.Span)!;
        var now = clock.GetCurrentInstant();
        var photo = await db.StaffPhotos.FirstOrDefaultAsync(p => p.StaffMemberId == staffMemberId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            db.StaffPhotos.Add(new StaffPhoto { StaffMemberId = staffMemberId, ContentType = contentType, Bytes = content.ToArray(), UpdatedAt = now });
        }
        else
        {
            photo.ContentType = contentType;
            photo.Bytes = content.ToArray();
            // Strictly later than the picture it replaces, whatever the clock says: an address is never reused.
            photo.UpdatedAt = Instant.Max(now, photo.UpdatedAt + Duration.FromMilliseconds(1));
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Staff photo set for staff member {StaffMemberId}: {Bytes} bytes of {ContentType}", staffMemberId, content.Length, contentType);
        return StaffPhotoOutcome.Saved;
    }

    public async Task<bool> RemoveAsync(Guid staffMemberId, CancellationToken cancellationToken = default)
    {
        if (await LockPersonAsync(staffMemberId, cancellationToken).ConfigureAwait(false) == 0)
        {
            return false;
        }

        // Deleted, not hidden (ADR 0088): the picture is the person's own data.
        var removed = await db.StaffPhotos.Where(p => p.StaffMemberId == staffMemberId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Staff photo removed for staff member {StaffMemberId}: {Rows} row(s) deleted", staffMemberId, removed);
        return true;
    }

    /// <summary>A no-change update that takes the row's lock; the count is 0 when the salon has no such person.</summary>
    private Task<int> LockPersonAsync(Guid staffMemberId, CancellationToken cancellationToken)
        => db.StaffMembers.Where(s => s.Id == staffMemberId).ExecuteUpdateAsync(s => s.SetProperty(m => m.SortOrder, m => m.SortOrder), cancellationToken);
}