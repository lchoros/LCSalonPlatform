using NodaTime;

using Salon.Core.Tenancy;
using Salon.Services.Tenancy;

namespace Salon.Services.Availability;

/// <summary>The current tenant's <see cref="DateTimeZone"/> (ADR 0002 gives the tenant its IANA zone id).</summary>
internal static class TenantZone
{
    public static async Task<DateTimeZone> ResolveAsync(ITenantContext tenantContext, ITenantLookup tenantLookup, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.TenantId
            ?? throw new TenantViolationException("Appointments are per tenant, but no tenant is set in the current scope.");
        var tenant = await tenantLookup.FindByIdAsync(tenantId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Tenant {tenantId} does not exist.");
        return DateTimeZoneProviders.Tzdb[tenant.TimeZoneId];
    }
}
