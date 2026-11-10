namespace Salon.Core.Tenancy;

/// <summary>
/// The current tenant for this request or job. Resolved from the subdomain on public pages and from the
/// signed-in user on admin pages. Jobs set it explicitly. Null means platform-level (no tenant) context.
/// </summary>
public interface ITenantContext
{
    Guid? TenantId { get; }

    bool HasTenant => TenantId is not null;
}