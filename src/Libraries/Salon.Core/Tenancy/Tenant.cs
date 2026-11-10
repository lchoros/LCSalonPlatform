using NodaTime;

using Salon.Core.Domain;

namespace Salon.Core.Tenancy;

/// <summary>One salon business account. Not itself tenant-scoped.</summary>
public class Tenant : BaseEntity
{
    /// <summary>URL-safe identifier used as the public booking subdomain, e.g. "studio-20".</summary>
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>IANA zone id, e.g. "Europe/London" or "Europe/Warsaw".</summary>
    public required string TimeZoneId { get; set; }

    /// <summary>Default UI culture: "en-GB" or "pl-PL".</summary>
    public required string Culture { get; set; }

    /// <summary>ISO 4217 code: "GBP" or "PLN".</summary>
    public required string Currency { get; set; }

    public Instant CreatedAt { get; set; }

    public bool IsActive { get; set; } = true;
}