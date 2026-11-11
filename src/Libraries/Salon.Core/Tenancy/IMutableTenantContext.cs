namespace Salon.Core.Tenancy;

/// <summary>
/// Sets the tenant for the current scope. Only infrastructure uses it: the HTTP tenant-resolution middleware,
/// the job runner and test helpers. Slices read <see cref="ITenantContext"/> and never change it.
/// </summary>
public interface IMutableTenantContext : ITenantContext
{
    void SetTenant(Guid? tenantId);
}