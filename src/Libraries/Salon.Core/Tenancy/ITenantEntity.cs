namespace Salon.Core.Tenancy;

/// <summary>Every entity owned by a salon. The global query filter in Salon.Data keys on <see cref="TenantId"/>.</summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}