using Salon.Core.Tenancy;

namespace Salon.Services.Tenancy;

/// <summary>Scoped holder of the current tenant. One instance per HTTP request, job execution or test scope.</summary>
internal sealed class TenantContext : IMutableTenantContext
{
    public Guid? TenantId { get; private set; }

    public void SetTenant(Guid? tenantId) => TenantId = tenantId;
}