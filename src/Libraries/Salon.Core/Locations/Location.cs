using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Locations;

/// <summary>
/// A physical salon address (glossary: "Lokalizacja"), ADR 0027. Tenant-owned. A salon starts with one, its main
/// location: where clients go and whom they call. The table can hold more; the first active row by
/// <see cref="SortOrder"/> is the main one. Times stay in the tenant's zone (ADR 0002): both markets have one zone each.
/// </summary>
public class Location : BaseEntity, ITenantEntity
{
    public const int NameMaxLength = 100;
    public const int AddressLineMaxLength = 200;
    public const int CityMaxLength = 100;
    public const int PostcodeMaxLength = 20;
    public const int PhoneMaxLength = 30;
    public const int EmailMaxLength = 254;

    public Guid TenantId { get; set; }

    /// <summary>What the place is called when it differs from the salon's name ("Studio 20 Shoreditch"); optional.</summary>
    public string? Name { get; set; }

    public required string AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public required string City { get; set; }

    public required string Postcode { get; set; }

    /// <summary>The number clients call, as the salon writes it; optional.</summary>
    public string? Phone { get; set; }

    /// <summary>The address clients write to; optional.</summary>
    public string? Email { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public Instant CreatedAt { get; set; }

    public Instant UpdatedAt { get; set; }
}