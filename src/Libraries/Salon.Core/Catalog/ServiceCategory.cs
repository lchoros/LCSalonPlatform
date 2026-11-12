using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Catalog;

/// <summary>
/// A heading the salon groups its services under ("Hair", "Nails"), ADR 0013. Tenant-owned. A service points at a
/// category through <see cref="Service.CategoryId"/>; deleting a category leaves its services uncategorised (SetNull).
/// An inactive category disappears from the pickers but keeps grouping the services that still reference it.
/// </summary>
public class ServiceCategory : BaseEntity, ITenantEntity
{
    public const int NameMaxLength = 100;

    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}