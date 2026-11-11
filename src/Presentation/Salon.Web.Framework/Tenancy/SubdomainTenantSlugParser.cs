using Microsoft.AspNetCore.Http;

namespace Salon.Web.Framework.Tenancy;

/// <summary>Extracts the tenant slug from "{slug}.{base-domain}". Pure function, unit tested.</summary>
public static class SubdomainTenantSlugParser
{
    public static bool TryGetSlug(HostString host, IEnumerable<string> baseDomains, out string slug)
    {
        ArgumentNullException.ThrowIfNull(baseDomains);
        slug = string.Empty;

        var hostName = host.Host;
        if (string.IsNullOrEmpty(hostName))
        {
            return false;
        }

        foreach (var baseDomain in baseDomains)
        {
            if (string.Equals(hostName, baseDomain, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var suffix = "." + baseDomain;
            if (!hostName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var candidate = hostName[..^suffix.Length];
            if (candidate.Length == 0
                || candidate.Contains('.', StringComparison.Ordinal)
                || string.Equals(candidate, "www", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            slug = candidate.ToLowerInvariant();
            return true;
        }

        return false;
    }
}