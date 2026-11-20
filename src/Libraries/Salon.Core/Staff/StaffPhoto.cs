using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Staff;

/// <summary>
/// The face of a staff member (ADR 0088): one picture per person, kept in the database because the pilot host's
/// deploy sync deletes files it did not ship. Tenant-owned, in its own table so a list of staff never drags the bytes
/// along. Personal data of the staff member: the row goes when the photo is removed and with the staff member.
/// </summary>
public class StaffPhoto : BaseEntity, ITenantEntity
{
    public const int ContentTypeMaxLength = 20;

    public Guid TenantId { get; set; }

    /// <summary>The person in the picture; one photo each.</summary>
    public Guid StaffMemberId { get; set; }

    /// <summary>What the bytes were found to be (<see cref="StaffPhotoRules.SniffContentType"/>), never what the browser claimed.</summary>
    public required string ContentType { get; set; }

    /// <summary>The picture exactly as uploaded: nothing resizes or re-encodes it.</summary>
    public required byte[] Bytes { get; set; }

    /// <summary>When this picture was put in place; a newer picture has a later one, which is what addresses are versioned by.</summary>
    public Instant UpdatedAt { get; set; }
}