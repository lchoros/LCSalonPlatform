using NodaTime;

using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Catalog;

/// <summary>A bookable service a salon offers, e.g. "Women's cut &amp; blow-dry". The first tenant-owned entity.</summary>
public class Service : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Total time blocked in the calendar. Processing time and buffers arrive in M1.</summary>
    public Duration Duration { get; set; }

    public decimal Price { get; set; }

    /// <summary>ISO 4217 code, normally the tenant's currency.</summary>
    public required string Currency { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public Money PriceAsMoney => new(Price, Currency);
}