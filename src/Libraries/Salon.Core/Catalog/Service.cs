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

    /// <summary>
    /// Total calendar time the client is in the salon, processing time included. The availability engine (ADR 0012)
    /// books <c>[start, start + Duration + BufferAfterMinutes)</c> for the stylist.
    /// </summary>
    public Duration Duration { get; set; }

    public decimal Price { get; set; }

    /// <summary>ISO 4217 code, normally the tenant's currency.</summary>
    public required string Currency { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>Heading on the lists (ADR 0013); null is "uncategorised", shown last.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>Clean-up time after the service (glossary: "Przerwa techniczna") when the stylist is still busy. 0–120, in 5-minute steps.</summary>
    public int BufferAfterMinutes { get; set; }

    /// <summary>
    /// Time inside the service when the client waits (colour develops) and the stylist is free for someone else
    /// (glossary: "Czas działania"). 0–240, in 5-minute steps; stored now, read by the availability engine later.
    /// </summary>
    public int ProcessingMinutes { get; set; }

    /// <summary>Where inside the service the processing window starts; <c>0 ≤ x ≤ Duration − ProcessingMinutes</c>. 0 when there is no processing time.</summary>
    public int ProcessingStartsAfterMinutes { get; set; }

    /// <summary>Deposit for this service as a percentage of the price (ADR 0023); null follows the salon's default, 0 means none.</summary>
    public decimal? DepositPercent { get; set; }

    public Money PriceAsMoney => new(Price, Currency);
}