namespace Salon.Services.Tenancy;

/// <summary>Read-only, cached access to tenant identity. Used by tenant resolution and anything that shows the salon's name.</summary>
public interface ITenantLookup
{
    Task<TenantInfo?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<TenantInfo?> FindByIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Immutable snapshot of a tenant; safe to cache and to hand to views.</summary>
public sealed record TenantInfo(Guid Id, string Slug, string Name, string TimeZoneId, string Culture, string Currency, bool IsActive);