using NodaTime;
using Salon.Core.Domain;
using Salon.Core.Tenancy;

namespace Salon.Core.Catalog;

/// <summary>
/// What one staff member does differently from the price list for one service (ADR 0018): their own duration, their own
/// price, or that they do not offer it at all. Tenant-owned; one row per (staff member, service). A null
/// <see cref="Duration"/> or <see cref="Price"/> means "the service default"; a row with <see cref="Offers"/> true and
/// both nulls (<see cref="IsDefault"/>) says nothing and is deleted rather than stored. The availability engine, the
/// booking snapshot and both booking pages read these through <c>IServiceOfferingResolver</c>, never the raw row.
/// </summary>
public class StaffServiceOverride : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid StaffMemberId { get; set; }

    public Guid ServiceId { get; set; }

    /// <summary>Total calendar time for this person, or null for <see cref="Service.Duration"/>. On the 5-minute grid, within the service bounds.</summary>
    public Duration? Duration { get; set; }

    /// <summary>This person's price in the service's currency, or null for <see cref="Service.Price"/>.</summary>
    public decimal? Price { get; set; }

    /// <summary>False when this person never does this service: no slots, skipped under "any staff", a booking refused.</summary>
    public bool Offers { get; set; } = true;

    /// <summary>True when the row changes nothing and need not exist.</summary>
    public bool IsDefault => Offers && Duration is null && Price is null;
}